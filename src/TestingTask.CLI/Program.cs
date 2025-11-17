using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestingTask.CLI;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();
    
var appSettings = config.GetSection("Settings").Get<AppSettings>();

Console.WriteLine(appSettings.ConnectionString);

var serviceCollection = new ServiceCollection();

serviceCollection.AddDbContext<MyDbContext>(options => {
    options.UseSqlServer(appSettings!.ConnectionString);
});

ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

using var context = serviceProvider.GetRequiredService<MyDbContext>();