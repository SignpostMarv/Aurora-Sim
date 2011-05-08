﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Xml;

using OpenSim.Framework;
using OpenSim.Services.Interfaces;

using log4net;
using Nini.Config;
using OpenMetaverse;

using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.CoreModules.Avatar.Inventory.Archiver;

namespace Aurora.Modules.World.DefaultInventoryIARLoader
{
    public class DefaultInventoryIARLoader : IDefaultLibraryLoader
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected ILibraryService m_service;
        protected IRegistryCore m_registry;
        protected Dictionary<string, AssetType> m_assetTypes = new Dictionary<string, AssetType>();

        public void LoadLibrary(ILibraryService service, IConfigSource source, IRegistryCore registry)
        {
            m_service = service;
            m_registry = registry;

            IConfig libConfig = source.Configs["InventoryIARLoader"];
            string pLibrariesLocation = "DefaultInventory/";
            AddDefaultAssetTypes();
            if (libConfig != null)
            {
                if (libConfig.GetBoolean("PreviouslyLoaded", false))
                    return; //If it is loaded, don't reload
                foreach (string iarFileName in Directory.GetFiles(pLibrariesLocation, "*.iar"))
                {
                    LoadLibraries(iarFileName);
                }
            }
        }

        private void AddDefaultAssetTypes()
        {
            m_assetTypes.Add("Animation", AssetType.Animation);
            m_assetTypes.Add("Bodypart", AssetType.Bodypart);
            m_assetTypes.Add("Body part", AssetType.Bodypart);
            m_assetTypes.Add("CallingCard", AssetType.CallingCard);
            m_assetTypes.Add("Calling Card", AssetType.CallingCard);
            m_assetTypes.Add("Clothing", AssetType.Clothing);
            m_assetTypes.Add("CurrentOutfit", AssetType.CurrentOutfitFolder);
            m_assetTypes.Add("Current Outfit", AssetType.CurrentOutfitFolder);
            m_assetTypes.Add("Gesture", AssetType.Gesture);
            m_assetTypes.Add("Landmark", AssetType.Landmark);
            m_assetTypes.Add("Script", AssetType.LSLText);
            m_assetTypes.Add("Scripts", AssetType.LSLText);
            m_assetTypes.Add("Mesh", AssetType.Mesh);
            m_assetTypes.Add("Notecard", AssetType.Notecard);
            m_assetTypes.Add("Object", AssetType.Object);
            m_assetTypes.Add("Photo", AssetType.SnapshotFolder);
            m_assetTypes.Add("Snapshot", AssetType.SnapshotFolder);
            m_assetTypes.Add("Sound", AssetType.Sound);
            m_assetTypes.Add("Texture", AssetType.Texture);
            m_assetTypes.Add("Images", AssetType.Texture);
        }

        /// <summary>
        /// Use the asset set information at path to load assets
        /// </summary>
        /// <param name="path"></param>
        /// <param name="assets"></param>
        protected void LoadLibraries(string iarFileName)
        {
            RegionInfo regInfo = new RegionInfo();
            Scene m_MockScene = null;
            //Make the scene for the IAR loader
            if (m_registry is Scene)
                m_MockScene = (Scene)m_registry;
            else
            {
                m_MockScene = new Scene();
                m_MockScene.Initialize (regInfo);
                m_MockScene.AddModuleInterfaces(m_registry.GetInterfaces());
            }

            UserAccount uinfo = m_MockScene.UserAccountService.GetUserAccount(UUID.Zero, m_service.LibraryOwner);
            //Make the user account for the default IAR
            if (uinfo == null)
            {
                m_log.Warn("Creating user " + m_service.LibraryOwnerName);
                m_MockScene.UserAccountService.CreateUser (m_service.LibraryOwner, m_service.LibraryOwnerName, "", "");
                uinfo = m_MockScene.UserAccountService.GetUserAccount (UUID.Zero, m_service.LibraryOwner);
                m_MockScene.InventoryService.CreateUserInventory(uinfo.PrincipalID, false);
            }
            if (m_MockScene.InventoryService.GetRootFolder(m_service.LibraryOwner) == null)
                m_MockScene.InventoryService.CreateUserInventory(uinfo.PrincipalID, false);

            InventoryCollection col = m_MockScene.InventoryService.GetFolderContent(uinfo.PrincipalID, UUID.Zero);
            bool alreadyExists = false;
            foreach (InventoryFolderBase folder in col.Folders)
            {
                if (folder.Name == iarFileName)
                {
                    alreadyExists = true;
                    break;
                }
            }
            if (alreadyExists)
            {
                m_log.InfoFormat("[LIBRARY INVENTORY]: Found previously loaded iar file {0}, ignoring.", iarFileName);
                return;
            }

            m_log.InfoFormat("[LIBRARY INVENTORY]: Loading iar file {0}", iarFileName);
            InventoryFolderBase rootFolder = m_MockScene.InventoryService.GetRootFolder(uinfo.PrincipalID);

            if (null == rootFolder)
            {
                //We need to create the root folder, otherwise the IAR freaks
                m_MockScene.InventoryService.CreateUserInventory(uinfo.PrincipalID, false);
            }

            InventoryArchiveReadRequest archread = new InventoryArchiveReadRequest(m_MockScene, uinfo, "/", iarFileName, false);

            try
            {
                List<InventoryNodeBase> nodes = new List<InventoryNodeBase>(archread.Execute(true));
                if (nodes.Count == 0)
                    return;
                InventoryFolderBase f = (InventoryFolderBase)nodes[0];

                TraverseFolders(nodes[0].ID, m_MockScene);
                //This is our loaded folder
                //Fix the name for later
                f.Name = iarFileName;
                f.ParentID = UUID.Zero;
                //f.Type = (int)AssetType.RootFolder;
                m_MockScene.InventoryService.UpdateFolder(f);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[LIBRARY MODULE]: Exception when processing archive {0}: {1}", iarFileName, e.StackTrace);
            }
            finally
            {
                archread.Close();
            }
        }

        private void TraverseFolders(UUID ID, Scene m_MockScene)
        {
            List<InventoryFolderBase> folders = m_MockScene.InventoryService.GetFolderFolders(m_service.LibraryOwner, ID);
            foreach (InventoryFolderBase folder in folders)
            {
                foreach (KeyValuePair<String, AssetType> type in m_assetTypes)
                {
                    if (folder.Name.ToLower ().StartsWith (type.Key.ToLower ()))
                    {
                        if (folder.Type == (short)type.Value)
                            break;
                        folder.Type = (short)type.Value;
                        m_MockScene.InventoryService.UpdateFolder (folder);
                        break;
                    }
                }
                TraverseFolders(folder.ID, m_MockScene);
            }
        }

        private void FixPerms(InventoryNodeBase node)
        {
            if (node is InventoryItemBase)
            {
                InventoryItemBase item = (InventoryItemBase)node;
                item.BasePermissions = 0x7FFFFFFF;
                item.EveryOnePermissions = 0x7FFFFFFF;
                item.CurrentPermissions = 0x7FFFFFFF;
                item.NextPermissions = 0x7FFFFFFF;
            }
        }

        private string GetInventoryPathFromName(string name)
        {
            string[] parts = name.Split(new char[] { ' ' });
            if (parts.Length == 3)
            {
                name = string.Empty;
                // cut the last part
                for (int i = 0; i < parts.Length - 1; i++)
                    name = name + ' ' + parts[i];
            }

            return name;
        }
    }
}
