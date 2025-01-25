﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using DLSS_Swapper.Data;
using DLSS_Swapper.Data.CustomDirectory;
using DLSS_Swapper.Data.EpicGamesStore;
using DLSS_Swapper.Data.GOG;
using DLSS_Swapper.Data.Steam;
using DLSS_Swapper.Data.UbisoftConnect;
using DLSS_Swapper.Data.Xbox;
using Nito.AsyncEx;
using SQLite;

namespace DLSS_Swapper;

internal class Database
{
    static Database? _instance;
    internal static Database Instance => _instance ??= new Database();

    internal AsyncLock Mutex { get; init; }
    internal SQLiteAsyncConnection Connection { get; init; }

    bool _hasInit = false;

    public Database()
    {
        Mutex = new AsyncLock();
        Connection = new SQLiteAsyncConnection(Storage.GetDBPath());
    }

    public void Init()
    {
        if (_hasInit)
        {
            return;
        }
        _hasInit = true;

        // Use a single syncronous connection to make tables
        using (var syncConnection = new SQLiteConnection(Storage.GetDBPath()))
        {
            try
            {
                syncConnection.CreateTable<SteamGame>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }


            try
            {
                syncConnection.CreateTable<GOGGame>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }


            try
            {
                syncConnection.CreateTable<EpicGamesStoreGame>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }


            try
            {
                syncConnection.CreateTable<UbisoftConnectGame>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }


            try
            {
                syncConnection.CreateTable<XboxGame>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }


            try
            {
                syncConnection.CreateTable<ManuallyAddedGame>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }

            try
            {
                syncConnection.CreateTable<GameAsset>();
            }
            catch (Exception err)
            {
                Logger.Error(err.Message);
            }

            syncConnection.Close();
        }
    }
    
}
