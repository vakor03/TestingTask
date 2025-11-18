using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;

namespace TestingTask.CLI;

public class MainExecutionThread(AppSettings appSettings)
{
    private readonly HashSet<DuplicateKey> _duplicates = new();
    private readonly List<CabData> _removedData = new();

    public void Run()
    {
        var parsedData = ReadCsvFile();

        var table = CreateDataTableSchema();

        ProcessData(parsedData, table);

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

    private IEnumerable<CabData> ReadCsvFile()
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
            CabData record = ReadRecord(csv);
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

    private void ProcessData(IEnumerable<CabData> cabDataList, DataTable table)
    {
        foreach (List<CabData> batch in cabDataList.BatchZeroCopy(appSettings.BatchSize))
            ProcessSingleBatch(batch, table);
    }

    private void ProcessSingleBatch(List<CabData> batch, DataTable dataTable)
    {
        foreach (CabData cabData in batch)
            dataTable.Rows.Add(ConvertToRow(dataTable, TransformDataToDTO(cabData)));

        BulkInsertTable(dataTable, appSettings.ConnectionString, appSettings.TableName);
        dataTable.Clear();
    }

    private static DuplicateKey CreateDuplicateKey(CabData cabData) =>
        new(cabData.tpep_pickup_datetime, cabData.tpep_dropoff_datetime,
            cabData.passenger_count);

    private bool DuplicateKeyAlreadyAdded(DuplicateKey duplicateKey) =>
        _duplicates.Contains(duplicateKey);

    private int BulkInsertTable(DataTable table, string connectionString, string tableName)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var bulk = new SqlBulkCopy(conn);
        InitializeBulkCopy(bulk, table.Rows.Count, tableName);

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

    private void InitializeBulkCopy(SqlBulkCopy bulk, int batchSize, string tableName)
    {
        bulk.DestinationTableName = tableName;
        bulk.BatchSize = batchSize;
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

    private static DataRow ConvertToRow(DataTable table, CabDataDTO cabData)
    {
        var row = table.NewRow();
        row["tpep_pickup_datetime"] = cabData.tpep_pickup_datetime;
        row["tpep_dropoff_datetime"] = cabData.tpep_dropoff_datetime;
        row["passenger_count"] = cabData.passenger_count;
        row["trip_distance"] = cabData.trip_distance;
        row["store_and_fwd_flag"] = cabData.store_and_fwd_flag;
        row["PULocationID"] = cabData.PULocationID;
        row["DOLocationID"] = cabData.DOLocationID;
        row["fare_amount"] = cabData.fare_amount;
        row["tip_amount"] = cabData.tip_amount;
        return row;
    }

    private static CabDataDTO TransformDataToDTO(CabData cabData)
    {
        DateTime pickupDatetime = cabData.tpep_pickup_datetime;
        DateTime dropoffDateTime = cabData.tpep_dropoff_datetime;
        string? storeAndFwdFlag = cabData.store_and_fwd_flag;
        storeAndFwdFlag = storeAndFwdFlag switch
        {
            "N" => "No",
            "Y" => "Yes",
            _ => SecurityUtils.SanitizeText(storeAndFwdFlag)
        };
        pickupDatetime = ConvertEstToUtc(pickupDatetime);
        dropoffDateTime = ConvertEstToUtc(dropoffDateTime);

        return new CabDataDTO()
        {
            tpep_pickup_datetime = pickupDatetime,
            tpep_dropoff_datetime = dropoffDateTime,
            passenger_count = SecurityUtils.ClampNonNegative(cabData.passenger_count),
            trip_distance = SecurityUtils.ClampNonNegative(cabData.trip_distance),
            store_and_fwd_flag = storeAndFwdFlag,
            PULocationID = SecurityUtils.ClampNonNegative(cabData.PULocationID),
            DOLocationID = SecurityUtils.ClampNonNegative(cabData.DOLocationID),
            fare_amount = SecurityUtils.ClampNonNegative(cabData.fare_amount),
            tip_amount = SecurityUtils.ClampNonNegative(cabData.tip_amount)
        };
    }

    private static CabData ReadRecord(CsvReader csv)
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

        var record = new CabData()
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

    private static DateTime ConvertEstToUtc(DateTime estDateTime)
    {
        TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        return TimeZoneInfo.ConvertTimeToUtc(estDateTime, estZone);
    }
}