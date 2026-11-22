using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<VentasDb>("db-ventas");
builder.AddAzureServiceBusClient("bus");

builder.Services.AddOpenApi(o => o.OpenApiVersion = OpenApiSpecVersion.OpenApi2_0);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<VentasDb>().Database.EnsureCreatedAsync();
}

var pedidos = app.MapGroup("/api/v1/pedidos").WithTags("Pedidos");

pedidos.MapGet("/", async (VentasDb db) =>
    TypedResults.Ok(await db.Pedidos.AsNoTracking().OrderBy(p => p.Id).ToListAsync()))
    .WithName("ListarPedidos");

pedidos.MapPost("/", async Task<Results<Created<Pedido>, ValidationProblem, Conflict<string>>> (
    CrearPedido entrada, VentasDb db, ServiceBusClient bus, ILogger<Program> log) =>
{
    var errores = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(entrada.Codigo)) errores["codigo"] = ["El código es obligatorio."];
    if (string.IsNullOrWhiteSpace(entrada.Cliente)) errores["cliente"] = ["El cliente es obligatorio."];
    if (entrada.Cantidad <= 0) errores["cantidad"] = ["La cantidad debe ser mayor que cero."];
    if (errores.Count > 0) return TypedResults.ValidationProblem(errores);

    if (await db.Pedidos.AnyAsync(p => p.Codigo == entrada.Codigo))
        return TypedResults.Conflict($"Ya existe el pedido {entrada.Codigo}.");
    var pedido = new Pedido
    {
        Codigo = entrada.Codigo.Trim(),
        Cliente = entrada.Cliente.Trim(),
        Producto = entrada.Producto.Trim(),
        Cantidad = entrada.Cantidad,
        Total = entrada.Total,
        FechaRegistro = DateTimeOffset.UtcNow
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    var evento = new PedidoRegistrado(pedido.Codigo, pedido.Cliente, pedido.Producto, pedido.Cantidad, pedido.Total);
    var mensaje = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(evento, JsonSerializerOptions.Web))
    {
        MessageId = pedido.Codigo,
        ContentType = "application/json",
        Subject = "PedidoRegistrado"
    };
    await using var emisor = bus.CreateSender("cola-pedidos");
    await emisor.SendMessageAsync(mensaje);

    log.LogInformation("Pedido {Codigo} guardado y publicado en {Cola}", pedido.Codigo, "cola-pedidos");
    return TypedResults.Created($"/api/v1/pedidos/{pedido.Id}", pedido);
})
.WithName("RegistrarPedido");

app.Run();
class VentasDb(DbContextOptions<VentasDb> options) : DbContext(options)
{
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    protected override void OnModelCreating(ModelBuilder m) => m.Entity<Pedido>().HasIndex(p => p.Codigo).IsUnique();
}
class Pedido
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Producto { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset FechaRegistro { get; set; }
}
record CrearPedido(string Codigo, string Cliente, string Producto, int Cantidad, decimal Total);
record PedidoRegistrado(string Codigo, string Cliente, string Producto, int Cantidad, decimal Total);