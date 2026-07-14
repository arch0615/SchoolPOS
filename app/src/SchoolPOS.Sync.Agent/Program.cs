using SchoolPOS.Data;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Sync.Agent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
