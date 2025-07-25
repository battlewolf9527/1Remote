﻿using System;
using System.Collections.Generic;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource.DAO.Dapper;
using _1RM.Service.DataSource.Model;
using _1RM.Utils;
using _1RM.View;
using Shawn.Utils;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
// ReSharper disable InconsistentNaming

namespace _1RM.Service.DataSource.DAO
{
    public enum DatabaseType
    {
        MySql,
        SqlServer,
        // ReSharper disable once IdentifierTypo
        // ReSharper disable once InconsistentNaming
        PostgreSQL,
        Oracle,
        // ReSharper disable once IdentifierTypo
        Sqlite,
    }


    public class Result
    {
        public bool IsSuccess = false;
        public bool NeedReloadUI = false;
        public string ErrorInfo = string.Empty;
        private static readonly Result _SUCCESS = new Result() { IsSuccess = true };
        public static Result Success()
        {
            return _SUCCESS;
        }

        public static string GetErrorInfo(string message, string databaseName, string reason)
        {
            return $@"{message.TrimEnd()} `{databaseName}`
{IoC.Translate("because:")}
{reason}";
        }
        public static Result Fail(string message, DataSourceBase? database, string reason)
        {
            return Fail(message, database?.DataSourceName ?? "", reason);
        }
        public static Result Fail(string message, string databaseName, string reason)
        {
            return new Result() { IsSuccess = false, ErrorInfo = GetErrorInfo(message, databaseName, reason) };
        }
        public static Result Fail(string message)
        {
            return new Result() { IsSuccess = false, ErrorInfo = message };
        }
    }

    public class ResultSelects<T> : Result
    {
        public readonly List<T> Items;

        public ResultSelects(List<T> items)
        {
            Items = items;
        }
        public static ResultSelects<T> Success(List<T> protocolBases)
        {
            return new ResultSelects<T>(protocolBases) { IsSuccess = true };
        }
        public new static ResultSelects<T> Fail(string message, string databaseName, string reason)
        {
            return new ResultSelects<T>(null!) { IsSuccess = false, ErrorInfo = GetErrorInfo(message, databaseName, reason) };
        }
        public new static ResultSelects<T> Fail(string message)
        {
            return new ResultSelects<T>(null!) { IsSuccess = false, ErrorInfo = message };
        }
    }

    public class ResultLong : Result
    {
        public readonly long Result;

        public ResultLong(long result)
        {
            Result = result;
        }
        public static ResultLong Success(long result)
        {
            return new ResultLong(result) { IsSuccess = true };
        }
        public new static ResultLong Fail(string message, string databaseName, string reason)
        {
            return new ResultLong(0) { IsSuccess = false, ErrorInfo = GetErrorInfo(message, databaseName, reason) };
        }
        public new static ResultLong Fail(string message)
        {
            return new ResultLong(0) { IsSuccess = false, ErrorInfo = message };
        }
    }
    public class ResultString : Result
    {
        public readonly string? Result;

        public ResultString(string? result)
        {
            Result = result;
        }
        public static ResultString Success(string? result)
        {
            return new ResultString(result) { IsSuccess = true };
        }
        public new static ResultString Fail(string message, string databaseName, string reason)
        {
            return new ResultString(string.Empty) { IsSuccess = false, ErrorInfo = GetErrorInfo(message, databaseName, reason) };
        }
        public new static ResultString Fail(string message)
        {
            return new ResultString(string.Empty) { IsSuccess = false, ErrorInfo = message };
        }
    }


    public abstract class IDatabase
    {
        public string DatabaseName;
        protected readonly DatabaseType DatabaseType;
        protected string _connectionString = "";

        protected IDatabase(string databaseName, DatabaseType databaseType)
        {
            DatabaseName = databaseName;
            DatabaseType = databaseType;
        }

        public abstract void CloseConnection();
        public abstract Result OpenNewConnection(string newConnectionString);

        public abstract bool IsConnected();

        /// <summary>
        /// create tables
        /// </summary>
        public abstract Result InitTables();

        public abstract Result TableExists(string tableName);

        //public abstract ResultSelect GetServer(int id);

        public abstract ResultSelects<ProtocolBase> GetServers();

        //public abstract ResultLong GetServerCount();

        /// <summary>
        /// insert and return id
        /// </summary>
        /// <param name="protocolBase"></param>
        /// <returns></returns>
        public abstract Result AddServer(ref ProtocolBase protocolBase);
        /// <summary>
        /// insert and return count
        /// </summary>
        /// <returns></returns>
        public abstract Result AddServer(IEnumerable<ProtocolBase> protocolBases);

        /// <summary>
        /// update server by id
        /// </summary>
        public abstract Result UpdateServer(ProtocolBase server);
        public abstract Result UpdateServer(IEnumerable<ProtocolBase> servers);

        public abstract Result DeleteServer(IEnumerable<string> ids);

        #region TableCredential

        public abstract ResultSelects<Credential> GetCredentials(bool closeInEnd = true);
        /// <summary>
        /// insert and return id
        /// </summary>
        public abstract Result AddCredential(ref Credential credential);


        /// <summary>
        /// update credential in database by Id, and update related protocols.
        /// </summary>
        /// <param name="credential">the credential to update</param>
        /// <param name="relatedProtocols">the protocols used this credential, will update the credential in these protocols</param>
        /// <returns></returns>
        public abstract Result UpdateCredential(Credential credential, List<ProtocolBaseWithAddressPortUserPwd>? relatedProtocols = null);
        //public abstract Result UpdateCredential(IEnumerable<Credential> credentials);

        public abstract Result DeleteCredential(IEnumerable<string> names, List<ProtocolBaseWithAddressPortUserPwd>? relatedProtocols = null);

        #endregion


        public abstract ResultString GetConfig(string key, bool closeInEnd = true);

        public abstract Result SetConfig(string key, string? value, bool closeInEnd = true);

        public abstract Result SetTableUpdateTimestamp(string tableName, long time = -1, bool closeInEnd = true);
        public abstract ResultLong GetTableUpdateTimestamp(string tableName, bool closeInEnd = true);

        protected void SetEncryptionTest()
        {
            var get = GetConfig("EncryptionTest");
            if (string.IsNullOrEmpty(get.Result))
            {
                SetConfig("EncryptionTest", UnSafeStringEncipher.SimpleEncrypt("EncryptionTest"));
            }
        }

        public bool CheckEncryptionTest()
        {
            var et = GetConfig("EncryptionTest");
            return UnSafeStringEncipher.SimpleDecrypt(et.Result ?? "") == "EncryptionTest";
        }
    }


    public static class DbExtensions
    {
        public static string GetSqliteConnectionString(string dbPath)
        {
            return $"Data Source={dbPath}; Pooling=true;Min Pool Size=1";
        }

        public static string GetMysqlConnectionString(string host, int port, string dbName, string user, string password, int connectTimeOutSeconds)
        {
            return $"server={host};port={port};database={dbName};Character Set=utf8;Uid={user};password={password};Connect Timeout={connectTimeOutSeconds};";
        }

        public static string GetPgsqlConnectionString(string host, int port, string dbName, string user, string password, int connectTimeOutSeconds)
        {
            return $"Host={host};Port={port};Database=\"{dbName}\";Username={user};Password={password};Timeout={connectTimeOutSeconds};";
        }

        //private static string? TryGetConfig(this IDatabase iDatabase, string key)
        //{
        //    try
        //    {
        //        var val = iDatabase.GetConfig(key) ?? "";
        //        return val;
        //    }
        //    catch (Exception e)
        //    {
        //        SimpleLogHelper.Error(e);
        //        return null;
        //    }
        //}

        //private static bool TrySetConfig(this IDatabase iDatabase, string key, string value)
        //{
        //    try
        //    {
        //        iDatabase.SetConfig(key, value ?? "");
        //        return true;
        //    }
        //    catch (Exception e)
        //    {
        //        SimpleLogHelper.Error(e);
        //        return false;
        //    }
        //}

        public static bool CheckWritable(this IDatabase iDatabase)
        {
            try
            {
                return iDatabase.SetConfig("permission_check", null).IsSuccess // delete
                && iDatabase.SetConfig("permission_check", "true").IsSuccess // insert
                 && iDatabase.SetConfig("permission_check", "true").IsSuccess; // update
            }
            catch (Exception e)
            {
                SimpleLogHelper.Info(e);
                return false;
            }
        }

        //public static bool CheckReadable(this IDatabase iDatabase)
        //{
        //    return iDatabase.GetConfig("EncryptionTest").IsSuccess;
        //}
    }
}