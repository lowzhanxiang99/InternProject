-- This adds the missing column manually so the app stops crashing
IF NOT EXISTS (SELECT * FROM sys.columns 
               WHERE Name = N'AttachmentPath' 
               AND Object_ID = Object_ID(N'LeaveRequests'))
BEGIN
    ALTER TABLE LeaveRequests ADD AttachmentPath nvarchar(max) NULL;
END