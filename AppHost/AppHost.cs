using AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddInMemoryFga();
builder.AddPostgresFga();
builder.AddMySqlFga();
builder.AddSqliteFga();

builder.Build().Run();