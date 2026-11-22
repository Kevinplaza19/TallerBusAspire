var builder = DistributedApplication.CreateBuilder(args);

var bus = builder.AddAzureServiceBus("bus")
    .RunAsEmulator(e => e.WithLifetime(ContainerLifetime.Persistent));

var cola = bus.AddServiceBusQueue("cola-pedidos");

var postgres = builder.AddPostgres("servidor-postgres")
    .WithImageTag("16-alpine")
    .WithDataVolume("bus_pg_datos")
    .WithLifetime(ContainerLifetime.Persistent);

var dbVentas = postgres.AddDatabase("db-ventas", "db_ventas");
var dbDespacho = postgres.AddDatabase("db-despacho", "db_despacho");

builder.AddDotnetProject("api-ventas", "../../src/Ventas.Api/Ventas.Api.csproj")
    .WithReference(dbVentas).WaitFor(dbVentas)
    .WithReference(bus).WaitFor(bus);

builder.AddDotnetProject("worker-despacho", "../../src/Despacho.Worker/Despacho.Worker.csproj")
    .WithReference(dbDespacho).WaitFor(dbDespacho)
    .WithReference(bus).WaitFor(bus);

builder.Build().Run();