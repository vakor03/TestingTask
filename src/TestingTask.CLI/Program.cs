using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
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

serviceCollection.AddDbContext<MyDbContext>(options => { options.UseSqlServer(appSettings!.ConnectionString); });

ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

using var context = serviceProvider.GetRequiredService<MyDbContext>();

WriteDuplicatesCsv(appSettings);

ReadCsvFile(appSettings);

var table = CreateDataTableSchema();

var row = table.NewRow();
row["tpep_pickup_datetime"] = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
row["tpep_dropoff_datetime"] = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
row["passenger_count"] = (byte)3;
row["trip_distance"] = (decimal)5.5;
row["store_and_fwd_flag"] = "N";
row["PULocationID"] = 2;
row["DOLocationID"] = 2;
row["fare_amount"] = 1.1d;
row["tip_amount"] = 2.2d;

BulkInsertTable(table, appSettings);

static void ReadCsvFile(AppSettings appSettings) {
    using var reader = new StreamReader(appSettings.InputFilePath);
    var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture) {
        HasHeaderRecord = true,
        IgnoreBlankLines = true,
        TrimOptions = TrimOptions.Trim,
        BadDataFound = null,
        MissingFieldFound = null
    };

    using var csv = new CsvReader(reader, csvConfiguration);
}

static void WriteDuplicatesCsv(AppSettings appSettings) {
    using var duplicatesWriter = new StreamWriter(appSettings.DuplicatesFilePath, false);
    using var duplicatesCsv = new CsvWriter(duplicatesWriter, CultureInfo.InvariantCulture);

    duplicatesCsv.WriteField("tpep_pickup_datetime");
    duplicatesCsv.WriteField("tpep_dropoff_datetime");
    duplicatesCsv.WriteField("passenger_count");
    duplicatesCsv.WriteField("trip_distance");
    duplicatesCsv.WriteField("store_and_fwd_flag");
    duplicatesCsv.WriteField("PULocationID");
    duplicatesCsv.WriteField("DOLocationID");
    duplicatesCsv.WriteField("fare_amount");
    duplicatesCsv.WriteField("tip_amount");
    duplicatesCsv.NextRecord();
}

static DataTable CreateDataTableSchema() {
    var dt = new DataTable();
    dt.Columns.Add(new DataColumn("tpep_pickup_datetime", typeof(DateTime))); // already UTC
    dt.Columns.Add(new DataColumn("tpep_dropoff_datetime", typeof(DateTime)));
    dt.Columns.Add(new DataColumn("passenger_count", typeof(byte)));
    dt.Columns.Add(new DataColumn("trip_distance", typeof(decimal)));
    dt.Columns.Add(new DataColumn("store_and_fwd_flag", typeof(string)));
    dt.Columns.Add(new DataColumn("PULocationID", typeof(int)));
    dt.Columns.Add(new DataColumn("DOLocationID", typeof(int)));
    dt.Columns.Add(new DataColumn("fare_amount", typeof(decimal)));
    dt.Columns.Add(new DataColumn("tip_amount", typeof(decimal)));
    return dt;
}

static int BulkInsertTable(DataTable table, AppSettings appSettings) {
    using var conn = new SqlConnection(appSettings.ConnectionString);
    conn.Open();
    return 1;
    // using var bulk = new SqlBulkCopy(conn) {
    //     DestinationTableName = "dbo.Trips",
    //     BatchSize = table.Rows.Count,
    //     BulkCopyTimeout = 600
    // };
    //
    // bulk.ColumnMappings.Add("tpep_pickup_datetime", "tpep_pickup_datetime");
    // bulk.ColumnMappings.Add("tpep_dropoff_datetime", "tpep_dropoff_datetime");
    // bulk.ColumnMappings.Add("passenger_count", "passenger_count");
    // bulk.ColumnMappings.Add("trip_distance", "trip_distance");
    // bulk.ColumnMappings.Add("store_and_fwd_flag", "store_and_fwd_flag");
    // bulk.ColumnMappings.Add("PULocationID", "PULocationID");
    // bulk.ColumnMappings.Add("DOLocationID", "DOLocationID");
    // bulk.ColumnMappings.Add("fare_amount", "fare_amount");
    // bulk.ColumnMappings.Add("tip_amount", "tip_amount");
    //
    // try {
    //     bulk.WriteToServer(table);
    //     return table.Rows.Count;
    // }
    // catch (Exception ex) {
    //     Console.WriteLine("Bulk insert failed: " + ex.Message);
    //     throw;
    // }
}