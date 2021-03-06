﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLua;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ESO_Discord_RichPresence_Client
{
    class SavedVariables
    {
        private static readonly string CustomPathSaveDirectory = Environment.ExpandEnvironmentVariables(@"%TEMP%\\ESO_DiscordRichPresence");
        static public string esoDir = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\\Documents\\Elder Scrolls Online");
        static public string Dir = $@"{esoDir}\\live\\SavedVariables";

        internal readonly Main Main;
        private readonly Discord _client;
        private readonly FolderBrowserDialog _browser;
        private readonly FileSystemWatcher _watcher;

        public string Path { get; set; } = "";

        public SavedVariables(Main form, Discord client, FolderBrowserDialog browser)
        {
            this.Main = form;
            this._client = client;
            this._browser = browser;
            this._watcher = new FileSystemWatcher();
        }

        public SavedVariables(Main form, Discord client, FolderBrowserDialog browser, string savedVarsPath)
        {
            this.Main = form;
            this._client = client;
            this._browser = browser;
            this._watcher = new FileSystemWatcher();
            this.Path = savedVarsPath;
        }

        public void Initialise()
        {
            this.Path = this.Main.Settings.CustomEsoLocation;
            this.EnsureSavedVarsExist();
            this.SetupWatcher();

            string LuaContents = File.ReadAllText(this.Path);
            Discord.CurrentCharacter = SavedVariables.ParseLua(LuaContents);
        }

        static public EsoCharacter ParseLua(string luaTable)
        {
            var LuaClient = new Lua();
            object[] result = null;

            try
            {
                result = LuaClient.DoString(luaTable);
            }

            catch (NLua.Exceptions.LuaException error)
            {
                var luaErrorResponse = MessageBox.Show($"Something went wrong while reading data from ESO: {error.Message}", "Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2);

                if (luaErrorResponse == DialogResult.Retry)
                    ParseLua(luaTable);
                else if (luaErrorResponse == DialogResult.Abort)
                    Application.Exit();
            }

            LuaTable rootTable = LuaClient.GetTable($"{Main.ADDON_NAME}_SavedVars");
            LuaTable defaultTable = (LuaTable)rootTable["Default"];

            Dictionary<object, LuaTable> Accounts = new Dictionary<object, LuaTable>();
            foreach (object key in defaultTable.Keys)
            {
                LuaTable value = (LuaTable)defaultTable[key];
                Accounts.Add(key, value);
            }

            return new EsoCharacter((LuaTable)Accounts.Values.First()["$AccountWide"]);
        }

        public void EnsureSavedVarsExist()
        {
            if (this.Path.Length > 0)
            {
                SavedVariables.esoDir = this.Path;
                SavedVariables.Dir = $@"{this.Path}\\live\\SavedVariables";
            }

            // "Elder Scrolls Online" doesn't exist in "My Documents"
            if (!Directory.Exists(SavedVariables.esoDir))
            {
                var response = MessageBox.Show("ESO Documents not found. Please select the \"Elder Scrolls Online\" folder in your Documents.", "File not found", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1);

                if (response == DialogResult.OK)
                {
                    if (this._browser.ShowDialog() == DialogResult.OK)
                    {
                        SavedVariables.esoDir = this._browser.SelectedPath;
                        SavedVariables.Dir = $@"{SavedVariables.esoDir}\\live\\SavedVariables";

                        this.Main.Settings["CustomEsoLocation"] = SavedVariables.esoDir;
                        this.Main.Settings.SaveToFile();
                    }

                    else
                        Environment.Exit(1);
                }

                else
                    Environment.Exit(1);
            }

            // if lua file doesn't exist in "SavedVariables"
            if (!File.Exists($@"{SavedVariables.Dir}\\{Main.ADDON_NAME}.lua"))
            {
                // if eso addon doesn't exist
                if (!Directory.Exists($@"{SavedVariables.esoDir}\\live\\AddOns\\{Main.ADDON_NAME}"))
                {
                    var addonResponse = MessageBox.Show($"The \"{Main.ADDON_NAME}\" AddOn was not detected in your Addons Folder. Download the AddOn and try again.", "AddOn Missing", MessageBoxButtons.RetryCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1);

                    if (addonResponse == DialogResult.Retry)
                        this.EnsureSavedVarsExist();
                    else
                        Environment.Exit(1);
                }

                // user probably hasn't run the addon, so no SavedVariables exist for our addon
                else
                {
                    var savedVarsResponse = MessageBox.Show($"Please run the \"{Main.ADDON_NAME}\" AddOn at least once. To do this, reload your UI in-game by typing \'/reloadui\' into the chat box.", "SavedVariables Missing", MessageBoxButtons.RetryCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1);

                    if (savedVarsResponse == DialogResult.Retry)
                        this.EnsureSavedVarsExist();
                    else
                        Environment.Exit(1);
                }
            }

            else
                this.Path = $@"{SavedVariables.Dir}\\{Main.ADDON_NAME}.lua";
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        private void SetupWatcher()
        {
            this._watcher.Path = $@"{SavedVariables.Dir}";
            this._watcher.Filter = $"{Main.ADDON_NAME}.lua";
            this._watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;

            this._watcher.Changed += new FileSystemEventHandler(this.OnChanged);
            this._watcher.Deleted += new FileSystemEventHandler(this.OnDeleted);
            this._watcher.Renamed += new RenamedEventHandler(this.OnRenamed);

            this._watcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            Console.WriteLine("SavedVariables file changed");
            
            // wait 1 second here to avoid conflicts with file being busy
            Thread.Sleep(1000);

            try
            {
                string LuaCharacter = File.ReadAllText(e.FullPath);
                Discord.CurrentCharacter = SavedVariables.ParseLua(LuaCharacter);
                this._client.UpdatePresence(Discord.CurrentCharacter);
            }

            catch (System.IO.IOException error)
            {
                var errorResponse = MessageBox.Show($"Something happened while updating your game: {error.Message}", "File Read Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2);

                if (errorResponse == DialogResult.Abort)
                    Environment.Exit(1);
                else if (errorResponse == DialogResult.Retry)
                    this.OnChanged(source, e);
            }
        }

        private void OnDeleted(object source, FileSystemEventArgs e)
        {
            Console.WriteLine("SavedVariables file deleted");
            this.EnsureSavedVarsExist();
        }

        private void OnRenamed(object source, RenamedEventArgs e)
        {
            Console.WriteLine("SavedVariables file renamed");
            this.EnsureSavedVarsExist();
        }
    }
}
