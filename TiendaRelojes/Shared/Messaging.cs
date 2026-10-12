using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Confluent.Kafka;
namespace Shared;
public record EventMessage(Guid Id, string Type, string Data, DateTime CreatedAt);
public record OrderLine(int ProductId, int Quantity, decimal UnitPrice);
public record OrderPlaced(Guid OrderId, List<OrderLine> Items);
public record StockReserved(Guid OrderId);
public record StockRejected(Guid OrderId, string Reason);
public record PaymentConfirmed(Guid OrderId);
public record PaymentFailed(Guid OrderId);
public record OrderCancelled(Guid OrderId);
public static class Events {
 public const string Topic="shop-events";
 public static string Json<T>(T value)=>JsonSerializer.Serialize(value);
 public static T Parse<T>(string value)=>JsonSerializer.Deserialize<T>(value)!;
 public static async Task Publish(IConfiguration config,string type,object data,CancellationToken ct=default){
  var options=new ProducerConfig{BootstrapServers=config["KAFKA_BOOTSTRAP"]??"localhost:9092",Acks=Acks.All,EnableIdempotence=true};
  using var producer=new ProducerBuilder<string,string>(options).Build();
  var msg=new EventMessage(Guid.NewGuid(),type,Json(data),DateTime.UtcNow);
  await producer.ProduceAsync(Topic,new Message<string,string>{Key=Guid.NewGuid().ToString(),Value=Json(msg)},ct);
 }
 public static IConsumer<string,string> Consumer(IConfiguration config,string group){
  var c=new ConsumerBuilder<string,string>(new ConsumerConfig{BootstrapServers=config["KAFKA_BOOTSTRAP"]??"localhost:9092",GroupId=group,AutoOffsetReset=AutoOffsetReset.Earliest,EnableAutoCommit=false}).Build();c.Subscribe(Topic);return c;
 }
}
