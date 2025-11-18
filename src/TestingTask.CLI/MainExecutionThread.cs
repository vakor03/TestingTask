using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace TestingTask.CLI;

public class MainExecutionThread(AppSettings appSettings, ILogger<MainExecutionThread> logger)
{
    private const int BULK_COPY_TIMEOUT = 600;

    private readonly HashSet<DuplicateKey> _duplicateKeys = new();
    private readonly List<CabData> _removedData = new();

    public void Run()
    {
        var parsedData = ReadCsvFile();

        var table = CreateDataTableSchema();

        ProcessData(parsedData, table, out int rowsInserted);

        WriteDuplicatesCsv();

        logger.LogInformation($"Inserted {rowsInserted} rows.");
    }

    private void WriteDuplicatesCsv()
    {
        using var duplicatesWriter = new StreamWriter(appSettings.DuplicatesFilePath, false);
        using var duplicatesCsv = new CsvWriter(duplicatesWriter, CultureInfo.InvariantCulture);

        duplicatesCsv.WriteRecords(_removedData);
        logger.LogInformation($"Duplicates written to file:///{Path.GetFullPath(appSettings.DuplicatesFilePath).Replace('\\', '/')}.");
    }

    private DataTable CreateDataTableSchema()
    {
        var dt = new DataTable();
        dt.Columns.Add(new DataColumn(ColumnNames.TpepPickupDatetime, typeof(DateTime)));
        dt.Columns.Add(new DataColumn(ColumnNames.TpepDropoffDatetime, typeof(DateTime)));
        dt.Columns.Add(new DataColumn(ColumnNames.PassengerCount, typeof(byte)));
        dt.Columns.Add(new DataColumn(ColumnNames.TripDistance, typeof(decimal)));
        dt.Columns.Add(new DataColumn(ColumnNames.StoreAndFwdFlag, typeof(string)));
        dt.Columns.Add(new DataColumn(ColumnNames.PuLocationId, typeof(int)));
        dt.Columns.Add(new DataColumn(ColumnNames.DoLocationId, typeof(int)));
        dt.Columns.Add(new DataColumn(ColumnNames.FareAmount, typeof(decimal)));
        dt.Columns.Add(new DataColumn(ColumnNames.TipAmount, typeof(decimal)));
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
                _duplicateKeys.Add(duplicateKey);
                yield return record;
            }
        }
    }

    private void ProcessData(IEnumerable<CabData> cabDataList, DataTable table, out int rowsInserted)
    {
        rowsInserted = 0;
        foreach (List<CabData> batch in cabDataList.BatchZeroCopy(appSettings.BatchSize))
        {
            ProcessSingleBatch(batch, table, out int rowsInsertedInBatch);
            rowsInserted += rowsInsertedInBatch;
        }
    }

    private void ProcessSingleBatch(List<CabData> batch, DataTable dataTable, out int rowsInserted)
    {
        foreach (CabData cabData in batch)
            dataTable.Rows.Add(ConvertToRow(dataTable, TransformDataToDTO(cabData)));

        rowsInserted = BulkInsertTable(dataTable, appSettings.ConnectionString, appSettings.TableName);
        dataTable.Clear();
    }

    private bool DuplicateKeyAlreadyAdded(DuplicateKey duplicateKey) =>
        _duplicateKeys.Contains(duplicateKey);

    private static DuplicateKey CreateDuplicateKey(CabData cabData) =>
        new(cabData.tpep_pickup_datetime, cabData.tpep_dropoff_datetime,
            cabData.passenger_count);

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
            logger.LogError("Bulk insert failed: " + ex.Message);
            throw;
        }
    }

    private void InitializeBulkCopy(SqlBulkCopy bulk, int batchSize, string tableName)
    {
        bulk.DestinationTableName = tableName;
        bulk.BatchSize = batchSize;
        bulk.BulkCopyTimeout = BULK_COPY_TIMEOUT;

        bulk.ColumnMappings.Add(ColumnNames.TpepPickupDatetime, ColumnNames.TpepPickupDatetime);
        bulk.ColumnMappings.Add(ColumnNames.TpepDropoffDatetime, ColumnNames.TpepDropoffDatetime);
        bulk.ColumnMappings.Add(ColumnNames.PassengerCount, ColumnNames.PassengerCount);
        bulk.ColumnMappings.Add(ColumnNames.TripDistance, ColumnNames.TripDistance);
        bulk.ColumnMappings.Add(ColumnNames.StoreAndFwdFlag, ColumnNames.StoreAndFwdFlag);
        bulk.ColumnMappings.Add(ColumnNames.PuLocationId, ColumnNames.PuLocationId);
        bulk.ColumnMappings.Add(ColumnNames.DoLocationId, ColumnNames.DoLocationId);
        bulk.ColumnMappings.Add(ColumnNames.FareAmount, ColumnNames.FareAmount);
        bulk.ColumnMappings.Add(ColumnNames.TipAmount, ColumnNames.TipAmount);
    }

    private static DataRow ConvertToRow(DataTable table, CabDataDTO cabData)
    {
        var row = table.NewRow();
        row[ColumnNames.TpepPickupDatetime] = cabData.tpep_pickup_datetime;
        row[ColumnNames.TpepDropoffDatetime] = cabData.tpep_dropoff_datetime;
        row[ColumnNames.PassengerCount] = cabData.passenger_count;
        row[ColumnNames.TripDistance] = cabData.trip_distance;
        row[ColumnNames.StoreAndFwdFlag] = cabData.store_and_fwd_flag;
        row[ColumnNames.PuLocationId] = cabData.PULocationID;
        row[ColumnNames.DoLocationId] = cabData.DOLocationID;
        row[ColumnNames.FareAmount] = cabData.fare_amount;
        row[ColumnNames.TipAmount] = cabData.tip_amount;
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
        csv.TryGetField<DateTime>(ColumnNames.TpepPickupDatetime, out var tpepPickupDatetime);
        csv.TryGetField<DateTime>(ColumnNames.TpepDropoffDatetime, out var tpepDropoffDatetime);
        csv.TryGetField<int>(ColumnNames.PassengerCount, out var passengerCount);
        csv.TryGetField<decimal>(ColumnNames.TripDistance, out var tripDistance);
        csv.TryGetField<string>(ColumnNames.StoreAndFwdFlag, out var storeAndFwdFlag);
        csv.TryGetField<int>(ColumnNames.PuLocationId, out var pulocationId);
        csv.TryGetField<int>(ColumnNames.DoLocationId, out var dolocationId);
        csv.TryGetField<decimal>(ColumnNames.FareAmount, out var fareAmount);
        csv.TryGetField<decimal>(ColumnNames.TipAmount, out var tipAmount);

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