using Microsoft.EntityFrameworkCore;
using Shared;
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<InventoryDb>(o=>o.UseSqlite(builder.Configuration.GetConnectionString("Db")??"Data Source=/data/inventory.db"));
builder.Services.AddHostedService<InventoryWorker>();
builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
var app=builder.Build();app.UseCors();
using(var scope=app.Services.CreateScope()){
 var db=scope.ServiceProvider.GetRequiredService<InventoryDb>();db.Database.EnsureCreated();
 if(!db.Products.Any()) {db.Products.AddRange(new Product {Name="Casio Rose Gold Diamond",Category="Casio",Price=32,Stock=12,Image="/images/rose.svg",Description="Reloj de estilo vintage en tono rosa dorado."},new Product{Name="Casio Old Money Plata",Category="Casio",Price=32,Stock=8,Image="/images/silver.svg",Description="Diseño clásico plateado con esfera clara."},new Product{Name="Casio Classic Dorado",Category="Casio",Price=32,Stock=10,Image="/images/gold.svg",Description="Reloj dorado de estilo elegante."},new Product{Name="Casio Classic Plata Azul",Category="Casio",Price=32,Stock=6,Image="/images/blue.svg",Description="Reloj plateado con esfera azul."});db.SaveChanges();}
}
app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
app.MapGet("/products",async(InventoryDb db)=>await db.Products.OrderBy(p=>p.Id).ToListAsync());
app.MapGet("/products/{id:int}",async(int id,InventoryDb db)=>await db.Products.FindAsync(id) is {} p?Results.Ok(p):Results.NotFound());
app.MapPost("/products",async(ProductInput input,InventoryDb db)=>{if(string.IsNullOrWhiteSpace(input.Name)||input.Price<0||input.Stock<0)return Results.BadRequest("Datos inválidos");var p=new Product{Name=input.Name,Category=input.Category,Price=input.Price,Stock=input.Stock,Image=input.Image,Description=input.Description};db.Products.Add(p);await db.SaveChangesAsync();return Results.Created($"/products/{p.Id}",p);});
app.MapPut("/products/{id:int}",async(int id,ProductInput input,InventoryDb db)=>{var p=await db.Products.FindAsync(id);if(p is null)return Results.NotFound();if(input.Price<0||input.Stock<0||string.IsNullOrWhiteSpace(input.Name))return Results.BadRequest();p.Name=input.Name;p.Category=input.Category;p.Price=input.Price;p.Stock=input.Stock;p.Image=input.Image;p.Description=input.Description;await db.SaveChangesAsync();return Results.Ok(p);});
app.MapDelete("/products/{id:int}",async(int id,InventoryDb db)=>{var p=await db.Products.FindAsync(id);if(p is null)return Results.NotFound();db.Products.Remove(p);await db.SaveChangesAsync();return Results.NoContent();});
app.MapGet("/movements",async(InventoryDb db)=>await db.Movements.OrderByDescending(m=>m.Id).Take(100).ToListAsync());
app.MapPost("/products/{id:int}/adjust",async(int id,AdjustRequest req,InventoryDb db)=>{var p=await db.Products.FindAsync(id);if(p is null)return Results.NotFound();if(p.Stock+req.Change<0)return Results.BadRequest("Stock insuficiente");p.Stock+=req.Change;db.Movements.Add(new StockMovement{ProductId=id,Change=req.Change,Reason=req.Reason??"Ajuste manual"});await db.SaveChangesAsync();return Results.Ok(p);});
app.Run();
record ProductInput(string Name,string Category,decimal Price,int Stock,string Image,string Description);
record AdjustRequest(int Change,string? Reason);
public class Product{public int Id{get;set;}public string Name{get;set;}="";public string Category{get;set;}="";public decimal Price{get;set;}public int Stock{get;set;}public int Reserved{get;set;}public string Image{get;set;}="";public string Description{get;set;}="";}
public class StockMovement{public int Id{get;set;}public int ProductId{get;set;}public int Change{get;set;}public string Reason{get;set;}="";public DateTime At{get;set;}=DateTime.UtcNow;}
public class Reservation{public int Id{get;set;}public Guid OrderId{get;set;}public int ProductId{get;set;}public int Quantity{get;set;}public string Status{get;set;}="reserved";}
public class ProcessedInventoryEvent{public int Id{get;set;}public Guid EventId{get;set;}}
public class InventoryDb(DbContextOptions<InventoryDb> options):DbContext(options){public DbSet<Product> Products=>Set<Product>();public DbSet<Reservation> Reservations=>Set<Reservation>();public DbSet<StockMovement> Movements=>Set<StockMovement>();public DbSet<ProcessedInventoryEvent> Processed=>Set<ProcessedInventoryEvent>();protected override void OnModelCreating(ModelBuilder b){b.Entity<ProcessedInventoryEvent>().HasIndex(x=>x.EventId).IsUnique();b.Entity<Reservation>().HasIndex(x=>new{x.OrderId,x.ProductId}).IsUnique();}}
public class InventoryWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<InventoryWorker> logger):BackgroundService{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken){await Task.Delay(3000,stoppingToken);while(!stoppingToken.IsCancellationRequested){try{using var consumer=Events.Consumer(config,"inventory-service");while(!stoppingToken.IsCancellationRequested){var result=consumer.Consume(stoppingToken);var ev=Events.Parse<EventMessage>(result.Message.Value);using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<InventoryDb>();if(await db.Processed.AnyAsync(x=>x.EventId==ev.Id,stoppingToken)){consumer.Commit(result);continue;}
 string? response=null;object? payload=null;
 if(ev.Type=="OrderPlaced"){
  var order=Events.Parse<OrderPlaced>(ev.Data);var valid=order.Items.Count>0;string reason="Stock insuficiente";
  foreach(var line in order.Items){var product=await db.Products.FindAsync(new object[]{line.ProductId},stoppingToken);if(product is null||line.Quantity<=0||product.Stock-product.Reserved<line.Quantity){valid=false;break;}}
  if(valid){foreach(var line in order.Items){var p=await db.Products.FindAsync(new object[]{line.ProductId},stoppingToken);p!.Reserved+=line.Quantity;db.Reservations.Add(new Reservation{OrderId=order.OrderId,ProductId=line.ProductId,Quantity=line.Quantity});}response="StockReserved";payload=new StockReserved(order.OrderId);}
  else{response="StockRejected";payload=new StockRejected(order.OrderId,reason);}
 }
 if(ev.Type is "PaymentConfirmed" or "PaymentFailed" or "OrderCancelled"){
  Guid orderId=ev.Type=="PaymentConfirmed"?Events.Parse<PaymentConfirmed>(ev.Data).OrderId:ev.Type=="PaymentFailed"?Events.Parse<PaymentFailed>(ev.Data).OrderId:Events.Parse<OrderCancelled>(ev.Data).OrderId;
  var reservations=await db.Reservations.Where(x=>x.OrderId==orderId&&x.Status=="reserved").ToListAsync(stoppingToken);
  foreach(var r in reservations){var p=await db.Products.FindAsync(new object[]{r.ProductId},stoppingToken);if(p is null)continue;p.Reserved-=r.Quantity;if(ev.Type=="PaymentConfirmed"){p.Stock-=r.Quantity;db.Movements.Add(new StockMovement{ProductId=p.Id,Change=-r.Quantity,Reason=$"Venta {orderId}"});}r.Status=ev.Type=="PaymentConfirmed"?"sold":"released";}
 }
 db.Processed.Add(new ProcessedInventoryEvent{EventId=ev.Id});await db.SaveChangesAsync(stoppingToken);
 if(response!=null)await Events.Publish(config,response,payload!,stoppingToken);
 consumer.Commit(result);
 }}catch(OperationCanceledException){break;}catch(Exception ex){logger.LogError(ex,"Inventory Kafka worker retrying");await Task.Delay(2500,stoppingToken);}}
 }
}
