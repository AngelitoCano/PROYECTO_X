using Microsoft.EntityFrameworkCore;
using Shared;
var builder=WebApplication.CreateBuilder(args);builder.Services.AddDbContext<OrdersDb>(o=>o.UseSqlite(builder.Configuration.GetConnectionString("Db")??"Data Source=/data/orders.db"));builder.Services.AddHostedService<OrderWorker>();builder.Services.AddHttpClient();builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));var app=builder.Build();app.UseCors();using(var s=app.Services.CreateScope())s.ServiceProvider.GetRequiredService<OrdersDb>().Database.EnsureCreated();
app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
app.MapGet("/orders",async(OrdersDb db)=>await db.Orders.Include(x=>x.Items).OrderByDescending(x=>x.CreatedAt).ToListAsync());
app.MapGet("/orders/{id:guid}",async(Guid id,OrdersDb db)=>await db.Orders.Include(x=>x.Items).FirstOrDefaultAsync(x=>x.Id==id) is {} order?Results.Ok(order):Results.NotFound());
app.MapPost("/orders",async(CreateOrder request,OrdersDb db,IHttpClientFactory http,IConfiguration config)=>{
 if(string.IsNullOrWhiteSpace(request.Customer)||string.IsNullOrWhiteSpace(request.Phone)||string.IsNullOrWhiteSpace(request.Address)||request.Items is null||request.Items.Count==0||request.Items.Count>30)return Results.BadRequest("Datos de pedido incompletos");
 var client=http.CreateClient();var baseUrl=config["INVENTORY_URL"]??"http://localhost:5101";var products=await client.GetFromJsonAsync<List<CatalogProduct>>(baseUrl+"/products")??new();
 var lines=new List<OrderItem>();foreach(var input in request.Items.GroupBy(x=>x.ProductId).Select(g=>new OrderInput(g.Key,g.Sum(x=>x.Quantity)))){var product=products.FirstOrDefault(x=>x.Id==input.ProductId);if(product is null||input.Quantity<=0||input.Quantity>100)return Results.BadRequest("Producto o cantidad inválida");lines.Add(new OrderItem{ProductId=product.Id,Name=product.Name,Quantity=input.Quantity,UnitPrice=product.Price});}
 var order=new ShopOrder{Id=Guid.NewGuid(),Customer=request.Customer,Phone=request.Phone,Address=request.Address,PaymentMethod=request.PaymentMethod=="contra-entrega"?"contra-entrega":"transferencia",Status="pending-stock",Items=lines,Total=lines.Sum(x=>x.UnitPrice*x.Quantity)};
 db.Orders.Add(order);await db.SaveChangesAsync();try{await Events.Publish(config,"OrderPlaced",new OrderPlaced(order.Id,lines.Select(x=>new OrderLine(x.ProductId,x.Quantity,x.UnitPrice)).ToList()));}catch{return Results.Problem("Pedido guardado, pero no se pudo publicar el evento. Requiere revisión.",statusCode:503);}return Results.Accepted($"/orders/{order.Id}",order);
});
app.MapPost("/orders/{id:guid}/cancel",async(Guid id,OrdersDb db,IConfiguration config)=>{var o=await db.Orders.FindAsync(id);if(o is null)return Results.NotFound();if(o.Status is not ("pending-stock" or "awaiting-payment"))return Results.BadRequest("El pedido ya no puede cancelarse");o.Status="cancelled";await db.SaveChangesAsync();await Events.Publish(config,"OrderCancelled",new OrderCancelled(id));return Results.Ok(o);});
app.Run();
record CreateOrder(string Customer,string Phone,string Address,string PaymentMethod,List<OrderInput> Items);
record OrderInput(int ProductId,int Quantity);
record CatalogProduct(int Id,string Name,decimal Price,int Stock,int Reserved);
public class ShopOrder{public Guid Id{get;set;}public string Customer{get;set;}="";public string Phone{get;set;}="";public string Address{get;set;}="";public string PaymentMethod{get;set;}="";public decimal Total{get;set;}public string Status{get;set;}="pending-stock";public DateTime CreatedAt{get;set;}=DateTime.UtcNow;public List<OrderItem> Items{get;set;}=new();}
public class OrderItem{public int Id{get;set;}public Guid ShopOrderId{get;set;}public int ProductId{get;set;}public string Name{get;set;}="";public int Quantity{get;set;}public decimal UnitPrice{get;set;}}
public class ProcessedOrderEvent{public int Id{get;set;}public Guid EventId{get;set;}}
public class OrdersDb(DbContextOptions<OrdersDb> options):DbContext(options){public DbSet<ShopOrder> Orders=>Set<ShopOrder>();public DbSet<ProcessedOrderEvent> Processed=>Set<ProcessedOrderEvent>();protected override void OnModelCreating(ModelBuilder b)=>b.Entity<ProcessedOrderEvent>().HasIndex(x=>x.EventId).IsUnique();}
public class OrderWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<OrderWorker> logger):BackgroundService{
 protected override async Task ExecuteAsync(CancellationToken ct){await Task.Delay(3000,ct);while(!ct.IsCancellationRequested){try{using var c=Events.Consumer(config,"orders-service");while(!ct.IsCancellationRequested){var result=c.Consume(ct);var ev=Events.Parse<EventMessage>(result.Message.Value);using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<OrdersDb>();if(await db.Processed.AnyAsync(x=>x.EventId==ev.Id,ct)){c.Commit(result);continue;}
 Guid? id=ev.Type switch{"StockReserved"=>Events.Parse<StockReserved>(ev.Data).OrderId,"StockRejected"=>Events.Parse<StockRejected>(ev.Data).OrderId,"PaymentConfirmed"=>Events.Parse<PaymentConfirmed>(ev.Data).OrderId,"PaymentFailed"=>Events.Parse<PaymentFailed>(ev.Data).OrderId,_=>null};
 if(id.HasValue){var order=await db.Orders.FindAsync(new object[]{id.Value},ct);if(order!=null&&order.Status!="cancelled"){
 if(ev.Type=="StockReserved"&&order.Status=="pending-stock")order.Status="awaiting-payment";
 if(ev.Type=="StockRejected"&&order.Status=="pending-stock")order.Status="rejected";
 if(ev.Type=="PaymentConfirmed"&&order.Status=="awaiting-payment")order.Status="paid";
 if(ev.Type=="PaymentFailed"&&order.Status=="awaiting-payment")order.Status="payment-failed";
 }}db.Processed.Add(new ProcessedOrderEvent{EventId=ev.Id});await db.SaveChangesAsync(ct);c.Commit(result);
 }}catch(OperationCanceledException){break;}catch(Exception ex){logger.LogError(ex,"Orders Kafka worker retrying");await Task.Delay(2500,ct);}}}
}
