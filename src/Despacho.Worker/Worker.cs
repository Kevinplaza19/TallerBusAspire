using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
namespace Despacho.Worker;

public class Worker(ServiceBusClient bus, IServiceScopeFactory scopes, ILogger<Worker> log) :
BackgroundService
{
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var procesador = bus.CreateProcessor("cola-pedidos", new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1
        });

        procesador.ProcessMessageAsync += ProcesarAsync;
        procesador.ProcessErrorAsync += e =>
        {
            log.LogError(e.Exception, "Error del bus en {Entidad}", e.EntityPath);
            return Task.CompletedTask;
        };

        await procesador.StartProcessingAsync(stoppingToken);
        log.LogInformation("Escuchando {Cola}", "cola-pedidos");

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await procesador.StopProcessingAsync();
    }
    private async Task ProcesarAsync(ProcessMessageEventArgs args)
    {
        var evento = JsonSerializer.Deserialize<PedidoRegistrado>(args.Message.Body.ToString(), JsonSerializerOptions.Web)!;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DespachoDb>();

        db.Ordenes.Add(new OrdenDespacho
        {
            CodigoPedido = evento.Codigo,
            Cliente = evento.Cliente,
            Producto = evento.Producto,
            Cantidad = evento.Cantidad,
            Estado = "Pendiente de alistamiento",
            FechaRecepcion = DateTimeOffset.UtcNow
        });
       await db.SaveChangesAsync();

       await args.CompleteMessageAsync(args.Message);
       log.LogInformation("Orden de despacho creada para el pedido {Codigo} (entrega n.º {Intento})",
        evento.Codigo, args.Message.DeliveryCount);
    }
}
public class DespachoDb(DbContextOptions<DespachoDb> options) : DbContext(options)
{
public DbSet<OrdenDespacho> Ordenes => Set<OrdenDespacho>();
}
public class OrdenDespacho
{
public int Id { get; set; }
public string CodigoPedido { get; set; } = "";
public string Cliente { get; set; } = "";
public string Producto { get; set; } = "";
public int Cantidad { get; set; }
public string Estado { get; set; } = "";
public DateTimeOffset FechaRecepcion { get; set; }
}
public record PedidoRegistrado(string Codigo, string Cliente, string Producto, int Cantidad, decimal Total);