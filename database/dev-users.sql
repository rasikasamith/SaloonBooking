-- Development only. Do not run this script against a production database.
USE [saloon-app-db];
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username=N'myuser')
  INSERT dbo.Users(Username,DisplayName,Role,PasswordSalt,PasswordHash,PasswordIterations)
  VALUES(N'myuser',N'My User',N'Customer',0x11A58483DD7B060999BD694CEDC73EAA,0x8AFE56502D7A1E4FCBDCF82F253FC2F153352F4266B31C4DE50A37373F6DBDA8,210000);
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username=N'myadmin')
  INSERT dbo.Users(Username,DisplayName,Role,PasswordSalt,PasswordHash,PasswordIterations)
  VALUES(N'myadmin',N'Amara Rodrigo',N'Admin',0xB9D0CA55564BE7FAE90C5D115CC3EDD6,0x91B6C480C80B9E2FEEEAAB08F8296A0C210446C00E6D900A9760DB17A85938E0,210000);
GO
