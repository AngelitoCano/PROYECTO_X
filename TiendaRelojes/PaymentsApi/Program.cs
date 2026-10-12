using Microsoft.EntityFrameworkCore;
using Shared;
var builder=WebApplication.CreateBuilder(args);builder.Services.AddDbContext<PaymentsDb>(o=>o.UseSqlite(builder.Configuration.GetConnectionString("Db")??"Data Source=/data/payments.db"));builder.Services.AddHostedService<PaymentsWorker>();builder.Services.AddHttpClient();builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));var app=builder.Build();app.UseCors();using(var s=app.Services.CreateScope())s.ServiceProvider.GetRequiredService<PaymentsDb>().Database.EnsureCreated();
app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
app.MapGet("/payments",async(PaymentsDb db)=>await db.Payments.OrderByDescending(x=>x.CreatedAt).ToListAsync());
app.MapGet("/payments/{id:guid}",async(Guid id,PaymentsDb db)=>await db.Payments.FirstOrDefaultAsync(x=>x.OrderId==id) is {} pay?Results.Ok(pay):Results.NotFound());
app.MapPost("/payments/{id:guid}/simulate",async(Guid id,SimulateRequest request,PaymentsDb db,IConfiguration config,IHttpClientFactory factory)=>{
 if(request.Result is not ("approved" or "rejected"))return Results.BadRequest("Resultado no válido");
 var order=await factory.CreateClient().GetFromJsonAsync<OrderSnapshot>((config["ORDERS_URL"]??"http://localhost:5102")+$"/orders/{id}");
 if(order is null||order.Status!="awaiting-payment")return Results.BadRequest("El pedido debe tener inventario reservado y estar pendiente de pago");
 var pay=await db.Payments.FirstOrDefaultAsync(x=>x.OrderId==id);if(pay is null){pay=new Payment{OrderId=id};db.Payments.Add(pay);}if(pay.Status!="pending")return Results.BadRequest("El pago ya fue procesado");
 pay.Status=request.Result;pay.Amount=order.Total;await db.SaveChangesAsync();
 await Events.Publish(config,request.Result=="approved"?"PaymentConfirmed":"PaymentFailed",request.Result=="approved"?new PaymentConfirmed(id):new PaymentFailed(id));return Results.Ok(pay);
});app.Run();
record SimulateRequest(string Result);
record OrderSnapshot(Guid Id,string Status,decimal Total);
public class Payment{public int Id{get;set;}public Guid OrderId{get;set;}public decimal Amount{get;set;}public string Status{get;set;}="pending";public DateTime CreatedAt{get;set;}=DateTime.UtcNow;}
public class ProcessedPaymentEvent{public int Id{get;set;}public Guid EventId{get;set;}}
public class PaymentsDb(DbContextOptions<PaymentsDb> options):DbContext(options){public DbSet<Payment> Payments=>Set<Payment>();public DbSet<ProcessedPaymentEvent> Processed=>Set<ProcessedPaymentEvent>();protected override void OnModelCreating(ModelBuilder b){b.Entity<Payment>().HasIndex(x=>x.OrderId).IsUnique();b.Entity<ProcessedPaymentEvent>().HasIndex(x=>x.EventId).IsUnique();}}
public class PaymentsWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<PaymentsWorker> logger):BackgroundService{
 protected override async Task ExecuteAsync(CancellationToken ct){await Task.Delay(3000,ct);while(!ct.IsCancellationRequested){try{using var c=Events.Consumer(config,"payments-service");while(!ct.IsCancellationRequested){var result=c.Consume(ct);var ev=Events.Parse<EventMessage>(result.Message.Value);using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PaymentsDb>();if(await db.Processed.AnyAsync(x=>x.EventId==ev.Id,ct)){c.Commit(result);continue;}
 if(ev.Type=="StockReserved"){var id=Events.Parse<StockReserved>(ev.Data).OrderId;if(!await db.Payments.AnyAsync(x=>x.OrderId==id,ct))db.Payments.Add(new Payment{OrderId=id});}
 db.Processed.Add(new ProcessedPaymentEvent{EventId=ev.Id});await db.SaveChangesAsync(ct);c.Commit(result);
 }}catch(OperationCanceledException){break;}catch(Exception ex){logger.LogError(ex,"Payments Kafka worker retrying");await Task.Delay(2500,ct);}}}
}
