using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;

namespace TestingTask.CLI;

public class MainExecutionThread(AppSettings appSettings)
{
    public void Run()
    {
        var foos = ReadCsvFile();

        var table = CreateDataTableSchema();

        ProcessData(foos, table);

        WriteDuplicatesCsv();
    }

    private void WriteDuplicatesCsv()
    {
        using var duplicatesWriter = new StreamWriter(appSettings.DuplicatesFilePath, false);
        using var duplicatesCsv = new CsvWriter(duplicatesWriter, CultureInfo.InvariantCulture);

        duplicatesCsv.WriteRecords(_removedData);
    }

    private DataTable CreateDataTableSchema()
    {
        var dt = new DataTable();
        dt.Columns.Add(new DataColumn("tpep_pickup_datetime", typeof(DateTime)));
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

    private IEnumerable<Foo> ReadCsvFile()
    {
        using var reader = new StreamReader(appSettings.InputFilePath);
        var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var csv = new CsvReader(reader, csvConfiguration);
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            Foo record = ReadRecord(csv);
            var duplicateKey = CreateDuplicateKey(record);
            if (DuplicateKeyAlreadyAdded(duplicateKey))
                _removedData.Add(record);
            else
            {
                _duplicates.Add(duplicateKey);
                yield return record;
            }
        }
    }

    private void ProcessData(IEnumerable<Foo> foos, DataTable table)
    {
        foreach (List<Foo> batch in foos.BatchZeroCopy(appSettings.BatchSize))
        {
            ProcessSingleBatch(batch, table);
        }
    }

    private void ProcessSingleBatch(List<Foo> batch, DataTable dataTable)
    {
        {
            foreach (Foo foo in batch)
                dataTable.Rows.Add(TransformDataAndConvertToRow(dataTable, foo));

            BulkInsertTable(dataTable, appSettings.ConnectionString);
            dataTable.Clear();
        }
    }

    private static DuplicateKey CreateDuplicateKey(Foo foo) =>
        new(foo.tpep_pickup_datetime, foo.tpep_dropoff_datetime,
            foo.passenger_count);

    private bool DuplicateKeyAlreadyAdded(DuplicateKey duplicateKey) =>
        _duplicates.Contains(duplicateKey);

    private HashSet<DuplicateKey> _duplicates = new();
    private List<Foo> _removedData = new();

    private int BulkInsertTable(DataTable table, string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var bulk = new SqlBulkCopy(conn);
        InitializeBulkCopy(bulk, table.Rows.Count);

        try
        {
            bulk.WriteToServer(table);
            return table.Rows.Count;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Bulk insert failed: " + ex.Message);
            throw;
        }
    }

    private static void InitializeBulkCopy(SqlBulkCopy bulk, int butchSize)
    {
        bulk.DestinationTableName = "dbo.Trips";
        bulk.BatchSize = butchSize;
        bulk.BulkCopyTimeout = 600;

        bulk.ColumnMappings.Add("tpep_pickup_datetime", "tpep_pickup_datetime");
        bulk.ColumnMappings.Add("tpep_dropoff_datetime", "tpep_dropoff_datetime");
        bulk.ColumnMappings.Add("passenger_count", "passenger_count");
        bulk.ColumnMappings.Add("trip_distance", "trip_distance");
        bulk.ColumnMappings.Add("store_and_fwd_flag", "store_and_fwd_flag");
        bulk.ColumnMappings.Add("PULocationID", "PULocationID");
        bulk.ColumnMappings.Add("DOLocationID", "DOLocationID");
        bulk.ColumnMappings.Add("fare_amount", "fare_amount");
        bulk.ColumnMappings.Add("tip_amount", "tip_amount");
    }

    private static DataRow TransformDataAndConvertToRow(DataTable table, Foo foo)
    {
        DateTime fooTpepPickupDatetime = foo.tpep_pickup_datetime;
        DateTime fooTpepDropoffDatetime = foo.tpep_dropoff_datetime;
        string? fooStoreAndFwdFlag = foo.store_and_fwd_flag;
        fooStoreAndFwdFlag = fooStoreAndFwdFlag switch
        {
            "N" => "No",
            "Y" => "Yes",
            _ => fooStoreAndFwdFlag
        };
        fooTpepPickupDatetime = ConvertEstToUtc(fooTpepPickupDatetime);
        fooTpepDropoffDatetime = ConvertEstToUtc(fooTpepDropoffDatetime);

        var row = table.NewRow();
        row["tpep_pickup_datetime"] = fooTpepPickupDatetime;
        row["tpep_dropoff_datetime"] = fooTpepDropoffDatetime;
        row["passenger_count"] = foo.passenger_count;
        row["trip_distance"] = foo.trip_distance;
        row["store_and_fwd_flag"] = fooStoreAndFwdFlag;
        row["PULocationID"] = foo.PULocationID;
        row["DOLocationID"] = foo.DOLocationID;
        row["fare_amount"] = foo.fare_amount;
        row["tip_amount"] = foo.tip_amount;
        return row;
    }

    private Foo ReadRecord(CsvReader csv)
    {
        csv.TryGetField<DateTime>("tpep_pickup_datetime", out var tpepPickupDatetime);
        csv.TryGetField<DateTime>("tpep_dropoff_datetime", out var tpepDropoffDatetime);
        csv.TryGetField<int>("passenger_count", out var passengerCount);
        csv.TryGetField<decimal>("trip_distance", out var tripDistance);
        csv.TryGetField<string>("store_and_fwd_flag", out var storeAndFwdFlag);
        csv.TryGetField<int>("PULocationID", out var pulocationId);
        csv.TryGetField<int>("DOLocationID", out var dolocationId);
        csv.TryGetField<decimal>("fare_amount", out var fareAmount);
        csv.TryGetField<decimal>("tip_amount", out var tipAmount);

        var record = new Foo()
        {
            tpep_pickup_datetime = tpepPickupDatetime,
            tpep_dropoff_datetime = tpepDropoffDatetime,
            passenger_count = passengerCount,
            trip_distance = tripDistance,
            store_and_fwd_flag = storeAndFwdFlag,
            PULocationID = pulocationId,
            DOLocationID = dolocationId,
            fare_amount = fareAmount,
            tip_amount = tipAmount
        };
        return record;
    }

    static DateTime ConvertEstToUtc(DateTime estDateTime)
    {
        TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        return TimeZoneInfo.ConvertTimeToUtc(estDateTime, estZone);
    }
}