IF DB_ID(N'saloon-app-db') IS NULL CREATE DATABASE [saloon-app-db];
GO
USE [saloon-app-db];
GO
IF OBJECT_ID(N'dbo.Services', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Services(Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,Name nvarchar(120) NOT NULL,Category nvarchar(60) NOT NULL,DurationMinutes int NOT NULL,Price decimal(10,2) NOT NULL,IsActive bit NOT NULL CONSTRAINT DF_Services_IsActive DEFAULT(1));
  INSERT dbo.Services(Name,Category,DurationMinutes,Price) VALUES
  (N'Haircut & blow dry',N'Hair',60,500),(N'Head massage',N'Wellness',45,600),(N'Hair coloring',N'Hair',120,7500),(N'Manicure',N'Nails',60,2200),(N'Beard trim',N'Grooming',30,1200);
END;
GO
IF OBJECT_ID(N'dbo.Appointments', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Appointments(Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,CustomerName nvarchar(160) NOT NULL,Phone nvarchar(40) NULL,ServiceId int NOT NULL,ServiceName nvarchar(120) NOT NULL,Price decimal(10,2) NOT NULL,DurationMinutes int NOT NULL,StartAt datetime2 NOT NULL,Status nvarchar(24) NOT NULL CONSTRAINT DF_Appointments_Status DEFAULT(N'Confirmed'),CreatedAt datetime2 NOT NULL CONSTRAINT DF_Appointments_CreatedAt DEFAULT(SYSUTCDATETIME()));
  CREATE INDEX IX_Appointments_StartAt ON dbo.Appointments(StartAt);
END;
GO
IF COL_LENGTH(N'dbo.Appointments', N'Phone') IS NULL
  ALTER TABLE dbo.Appointments ADD Phone nvarchar(40) NULL;
GO
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Users
  (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Username nvarchar(50) NOT NULL,
    DisplayName nvarchar(120) NOT NULL,
    Role nvarchar(20) NOT NULL,
    PasswordSalt varbinary(16) NOT NULL,
    PasswordHash varbinary(32) NOT NULL,
    PasswordIterations int NOT NULL CONSTRAINT DF_Users_PasswordIterations DEFAULT(210000),
    IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT(SYSUTCDATETIME()),
    CONSTRAINT CK_Users_Role CHECK (Role IN (N'Customer', N'Admin'))
  );
  CREATE UNIQUE INDEX UX_Users_Username ON dbo.Users(Username);
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username=N'myuser')
  INSERT dbo.Users(Username,DisplayName,Role,PasswordSalt,PasswordHash,PasswordIterations)
  VALUES(N'myuser',N'My User',N'Customer',0x11A58483DD7B060999BD694CEDC73EAA,0x8AFE56502D7A1E4FCBDCF82F253FC2F153352F4266B31C4DE50A37373F6DBDA8,210000);
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username=N'myadmin')
  INSERT dbo.Users(Username,DisplayName,Role,PasswordSalt,PasswordHash,PasswordIterations)
  VALUES(N'myadmin',N'Amara Rodrigo',N'Admin',0xB9D0CA55564BE7FAE90C5D115CC3EDD6,0x91B6C480C80B9E2FEEEAAB08F8296A0C210446C00E6D900A9760DB17A85938E0,210000);
GO
