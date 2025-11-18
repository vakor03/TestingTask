using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestingTask.CLI;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var appSettings = config.GetSection("Settings").Get<AppSettings>()!;

var serviceCollection = new ServiceCollection();

serviceCollection.AddSingleton<AppSettings>(appSettings);
serviceCollection.AddSingleton<MainExecutionThread>();

ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

var mainExecutionThread = serviceProvider.GetRequiredService<MainExecutionThread>();
mainExecutionThread.Run();