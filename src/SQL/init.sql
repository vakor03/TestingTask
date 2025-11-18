CREATE DATABASE TaxiETL;
GO

USE TaxiETL;
GO

CREATE TABLE dbo.Trips
(
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    tpep_pickup_datetime DATETIME2(3) NOT NULL,
    tpep_dropoff_datetime DATETIME2(3) NOT NULL,
    passenger_count TINYINT NOT NULL,
    trip_distance DECIMAL(9,3) NULL,
    store_and_fwd_flag NVARCHAR(3) NOT NULL,
    PULocationID INT NULL,
    DOLocationID INT NULL,
    fare_amount DECIMAL(10,2) NULL,
    tip_amount DECIMAL(10,2) NULL,

    trip_duration_seconds AS (DATEDIFF(SECOND, tpep_pickup_datetime, tpep_dropoff_datetime)) PERSISTED
);

CREATE NONCLUSTERED INDEX IX_Trips_PULocationID_TipAmount
    ON dbo.Trips (PULocationID) INCLUDE (tip_amount);

CREATE NONCLUSTERED INDEX IX_Trips_TripDistance
    ON dbo.Trips (trip_distance DESC);

CREATE NONCLUSTERED INDEX IX_Trips_TripDuration
    ON dbo.Trips (trip_duration_seconds DESC);