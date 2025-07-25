﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Service.DataSource.DAO;
using _1RM.Service.DataSource.DAO.Dapper;
using _1RM.Service.DataSource.Model;
using _1RM.Utils.Tracing;
using _1RM.View;
using Newtonsoft.Json;
using Shawn.Utils;
using Shawn.Utils.Wpf.FileSystem;

namespace _1RM.Service.DataSource
{
    public class DataSourceService : NotifyPropertyChangedBase
    {
        public const int CHECK_UPDATE_PERIOD = 5;
        public const string LOCAL_DATA_SOURCE_NAME = "Local";

        public DataSourceService()
        {
        }

        public SqliteSource? LocalDataSource { get; private set; } = null;

        public readonly ConcurrentDictionary<string, DataSourceBase> AdditionalSources = new ConcurrentDictionary<string, DataSourceBase>();

        public List<ProtocolBaseViewModel> GetServers(bool force)
        {
            lock (this)
            {
                var ret = new List<ProtocolBaseViewModel>(100);
                if (LocalDataSource != null)
                    ret.AddRange(LocalDataSource.GetServers(force));
                foreach (var dataSource in AdditionalSources)
                {
                    try
                    {
                        var pbs = dataSource.Value.GetServers(force);
                        foreach (var pb in pbs)
                        {
                            pb.DataSourceNameForLauncher = AdditionalSources.Any() ? pb.DataSourceName : "";
                        }
                        ret.AddRange(pbs);
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.DebugWarning(e);
                    }
                }
                return ret;
            }
        }

        public List<Credential> GetCredentials(bool force)
        {
            lock (this)
            {
                var ret = new List<Credential>(100);
                if (LocalDataSource != null)
                    ret.AddRange(LocalDataSource.GetCredentials(force));
                foreach (var dataSource in AdditionalSources)
                {
                    try
                    {
                        var cs = dataSource.Value.GetCredentials(force);
                        ret.AddRange(cs);
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.DebugWarning(e);
                    }
                }
                return ret;
            }
        }


        public List<(DataSourceBase, Credential)> GetSourceCredentials(bool force)
        {
            lock (this)
            {
                var ret = new List<(DataSourceBase, Credential)>(100);
                if (LocalDataSource != null)
                {
                    ret.AddRange(LocalDataSource.GetCredentials().Select(credential => (LocalDataSource, credential)).Select(dummy => ((DataSourceBase, Credential)) dummy));
                }
                foreach (var dataSource in AdditionalSources)
                {
                    try
                    {
                        var cs = dataSource.Value.GetCredentials(force);
                        ret.AddRange(cs.Select(credential => (dataSource.Value, credential)).Select(dummy => ((DataSourceBase, Credential))dummy));
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.DebugWarning(e);
                    }
                }
                return ret;
            }
        }


        public DataSourceBase? GetDataSource(string sourceId = LOCAL_DATA_SOURCE_NAME)
        {
            if (string.IsNullOrEmpty(sourceId) || sourceId == LOCAL_DATA_SOURCE_NAME)
                return LocalDataSource;
            if (AdditionalSources.ContainsKey(sourceId))
                return AdditionalSources[sourceId];
            return null;
        }


        /// <summary>
        /// init db connection to a sqlite db. Do make sure sqlitePath is writable!.
        /// </summary>
        public DatabaseStatus InitLocalDataSource(SqliteSource sqliteConfig)
        {
            LocalDataSource?.Database_CloseConnection();
            sqliteConfig.DataSourceName = LOCAL_DATA_SOURCE_NAME;
            sqliteConfig.ClearReadTimestamp();
            if (!IoPermissionHelper.HasWritePermissionOnFile(sqliteConfig.Path))
            {
                LocalDataSource = null;
                return DatabaseStatus.New(EnumDatabaseStatus.AccessDenied, $"write permission denied for `{sqliteConfig.Path}`");
            }
            var ret = sqliteConfig.Database_SelfCheck();
            LocalDataSource = sqliteConfig;
            RaisePropertyChanged(nameof(LocalDataSource));
            return ret;
        }

        /// <summary>
        /// 添加并启用一个新的数据源（如果该数据源已存在，则更新），返回数据源的连接状态
        /// </summary>
        public DatabaseStatus AddOrUpdateDataSource(DataSourceBase newDataSource, int connectTimeOutSeconds = 2, bool doReload = true)
        {
            try
            {
                newDataSource.ClearReadTimestamp(); // reload database
                if (newDataSource is SqliteSource { DataSourceName: LOCAL_DATA_SOURCE_NAME } localConfig)
                {
                    return InitLocalDataSource(localConfig);
                }

                // remove the old one
                {
                    var olds = AdditionalSources.Where(x => x.Value == newDataSource);
                    foreach (var pair in olds)
                    {
                        AdditionalSources.TryRemove(pair.Key, out var tmp);
                        tmp?.Database_CloseConnection();
                    }

                    {
                        AdditionalSources.TryRemove(newDataSource.DataSourceName, out var tmp);
                        tmp?.Database_CloseConnection();
                    }
                }


                newDataSource.Database_CloseConnection();
                var ret = DatabaseStatus.New(EnumDatabaseStatus.NotConnectedYet);
                if (connectTimeOutSeconds > 0)
                    ret = newDataSource.Database_SelfCheck(connectTimeOutSeconds);
                AdditionalSources.AddOrUpdate(newDataSource.DataSourceName, newDataSource, (name, source) => newDataSource);
                return ret;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning(e);
                UnifyTracing.Error(e);
                var ret = DatabaseStatus.New(EnumDatabaseStatus.OtherError, e.Message);
                return ret;
            }
            finally
            {
                if (doReload)
                    IoC.Get<GlobalData>().ReloadAll(); // reload server list after adding a new data source
            }
        }

        public void RemoveDataSource(string name)
        {
            if (name == LOCAL_DATA_SOURCE_NAME)
                return;
            else if (AdditionalSources.ContainsKey(name))
            {
                AdditionalSources[name].Database_CloseConnection();
                if (AdditionalSources.TryRemove(name, out _))
                {
                    IoC.Get<GlobalData>().ReloadAll(true); // reload server list after removing a data source
                }
            }
        }

        public static List<DataSourceBase> AdditionalSourcesLoadFromProfile(string path)
        {
            var ads = new List<DataSourceBase>();
            if (File.Exists(path))
            {
                var tmp = JsonConvert.DeserializeObject<List<DataSourceBase>>(File.ReadAllText(AppPathHelper.Instance.ProfileAdditionalDataSourceJsonPath));
                if (tmp != null)
                    ads = tmp;
            }
            return ads;
        }


        public static void AdditionalSourcesSaveToProfile(string path, List<DataSourceBase> sources)
        {
            try
            {
                if (sources.Count == 0)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                        fi.Delete();
                }
                else
                {
                    var fi = new FileInfo(path);
                    if (fi?.Directory?.Exists == false)
                        fi.Directory.Create();
                    if (IoPermissionHelper.HasWritePermissionOnFile(path))
                        RetryHelper.Try(() =>
                        {
                            File.WriteAllText(path, JsonConvert.SerializeObject(sources, Formatting.Indented), Encoding.UTF8);
                        }, actionOnError: exception => UnifyTracing.Error(exception));
                }
            }
            finally
            {

            }
        }
    }

    //public static class DataSourceServiceExtend
    //{
    //    public static DataSourceBase? GetDataSource(this ProtocolBase protocol)
    //    {
    //        return protocol.DataSource;
    //        //return IoC.Get<DataSourceService>().GetDataSource(protocol.DataSource);
    //    }
    //    public static DataSourceBase? GetDataSource(this ProtocolBaseViewModel protocol)
    //    {
    //        return protocol.DataSource;
    //        //return IoC.Get<DataSourceService>().GetDataSource(protocol.Server.DataSource);
    //    }
    //}
}