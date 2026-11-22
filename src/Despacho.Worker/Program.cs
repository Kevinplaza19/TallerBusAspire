using Despacho.Worker;

AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<DespachoDb>("db-despacho");
builder.AddAzureServiceBusClient("bus");

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<DespachoDb>().Database.EnsureCreated();
}
host.Run();