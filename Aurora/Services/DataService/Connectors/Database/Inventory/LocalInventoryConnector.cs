/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Aurora.Framework;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Services.Interfaces;

namespace Aurora.Services.DataService
{
    public class LocalInventoryConnector : IInventoryData
    {
        protected IGenericData GD;
        protected IRegistryCore m_registry;
        protected string m_foldersrealm = "inventoryfolders";
        protected string m_itemsrealm = "inventoryitems";

        #region IInventoryData Members

        public virtual void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore simBase, string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("InventoryConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;

                string connectionString = defaultConnectionString;
                if (source.Configs[Name] != null)
                    connectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                GD.ConnectToDatabase(connectionString, "Inventory",
                                     source.Configs["AuroraConnectors"].GetBoolean("ValidateTables", true));

                DataManager.DataManager.RegisterPlugin(this);
            }
            m_registry = simBase;
        }

        public string Name
        {
            get { return "IInventoryData"; }
        }

        public virtual List<InventoryFolderBase> GetFolders(string[] fields, string[] vals)
        {
            QueryFilter filter = new QueryFilter();
            for (int i = 0; i < fields.Length; ++i)
            {
                filter.andFilters[fields[i]] = vals[i];
            }

            return ParseInventoryFolders(GD.Query(new string[1]{ "*" }, m_foldersrealm, filter, null, null, null));
        }

        public virtual List<InventoryItemBase> GetItems(string[] fields, string[] vals)
        {
            QueryFilter filter = new QueryFilter();

            for (int i = 0; i < fields.Length; i++)
            {
                filter.andFilters[fields[i]] = vals[i];
//                query += String.Format("where {0} = '{1}' and ", fields[i], vals[i]);
                i++;
            }

            try
            {
                return ParseInventoryItems(GD.Query(new string[1] { "*" }, m_itemsrealm, filter, null, null, null));
            }
            catch { }
            finally
            {
                GD.CloseDatabase();
            }

            return null;
        }

        public virtual OSDArray GetLLSDItems(string[] fields, string[] vals)
        {
            QueryFilter filter = new QueryFilter();

//            string query = "";
            for (int i = 0; i < fields.Length; i++)
            {
                filter.andFilters[fields[i]] = vals[i];
//                query += String.Format("where {0} = '{1}' and ", fields[i], vals[i]);
                i++;
            }
//            query = query.Remove(query.Length - 5);
            try
            {
                return ParseLLSDInventoryItems(GD.Query(new string[1] { "*" }, m_itemsrealm, filter, null, null, null));
            }
            catch { }
            finally
            {
                GD.CloseDatabase();
            }
            return null;
        }

        public virtual bool HasAssetForUser(UUID userID, UUID assetID)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["assetID"] = assetID;
            filter.andFilters["avatarID"] = userID;

            List<string> q = GD.Query(new string[1] { "*" }, m_itemsrealm, filter, null, null, null);

            return !(q != null && q.Count > 0);
        }

        public virtual string GetItemNameByAsset(UUID assetID)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["assetID"] = assetID;

            List<string> q = GD.Query(new string[1] { "inventoryName" }, m_itemsrealm, filter, null, null, null);


            return (q != null && q.Count > 0) ? q[0] :  "";
        }

        public virtual byte[] FetchInventoryReply(OSDArray fetchRequest, UUID AgentID, UUID forceOwnerID, UUID libraryOwnerID)
        {
            LLSDSerializationDictionary contents = new LLSDSerializationDictionary();
            contents.WriteStartMap("llsd"); //Start llsd

            contents.WriteKey("folders"); //Start array items
            contents.WriteStartArray("folders"); //Start array folders

            foreach (OSD m in fetchRequest)
            {
                contents.WriteStartMap("internalContents"); //Start internalContents kvp
                OSDMap invFetch = (OSDMap) m;

                //UUID agent_id = invFetch["agent_id"].AsUUID();
                UUID owner_id = invFetch["owner_id"].AsUUID();
                UUID folder_id = invFetch["folder_id"].AsUUID();
                bool fetch_folders = invFetch["fetch_folders"].AsBoolean();
                bool fetch_items = invFetch["fetch_items"].AsBoolean();
                int sort_order = invFetch["sort_order"].AsInteger();

                //Set the normal stuff
                contents["agent_id"] = AgentID;
                contents["owner_id"] = owner_id;
                contents["folder_id"] = folder_id;

                contents.WriteKey("items"); //Start array items
                contents.WriteStartArray("items");
                List<UUID> moreLinkedItems = new List<UUID>();
                int count = 0;
                bool addToCount = true;
                QueryFilter filter = new QueryFilter();
                filter.andFilters["parentFolderID"] = folder_id;
                filter.orMultiFilters["avatarID"] = new List<object>
                {
                    AgentID,
                    libraryOwnerID
                };
                redoQuery:
                List<string> retVal = GD.Query(new string[1] { "*" }, m_itemsrealm, filter, null, null, null);
                try
                {
                    if (retVal.Count % 20 == 0)
                    {
                        for (int i = 0; i < retVal.Count; i += 20)
                        {
                            UUID avatarID = forceOwnerID == UUID.Zero ? UUID.Parse(retVal[i + 17]) : forceOwnerID;

                            #region item

                            contents.WriteStartMap("item"); //Start item kvp
                            UUID assetID = UUID.Parse(retVal[i]);
                            contents["asset_id"] = assetID;
                            contents["name"] = retVal[i + 2];
                            contents["desc"] = retVal[i + 3];

                            #region permissions

                            contents.WriteKey("permissions"); //Start permissions kvp
                            contents.WriteStartMap("permissions");
                            contents["group_id"] = UUID.Parse(retVal[i + 13]);
                            contents["is_owner_group"] = int.Parse(retVal[i + 14]) == 1;
                            contents["group_mask"] = uint.Parse(retVal[i + 19]);
                            contents["owner_id"] = avatarID;
                            contents["last_owner_id"] = UUID.Parse(retVal[i + 17]);
                            contents["next_owner_mask"] = uint.Parse(retVal[i + 4]);
                            contents["owner_mask"] = uint.Parse(retVal[i + 5]);

                            UUID creator;
                            if (UUID.TryParse(retVal[i + 7], out creator))
                            {
                                contents["creator_id"] = creator;
                            }
                            else
                            {
                                contents["creator_id"] = UUID.Zero;
                            }

                            contents["base_mask"] = uint.Parse(retVal[i + 8]);
                            contents["everyone_mask"] = uint.Parse(retVal[i + 9]);
                            contents.WriteEndMap();

                            #endregion

                            #region sale_info

                            contents.WriteKey("sale_info"); //Start permissions kvp
                            contents.WriteStartMap("sale_info"); //Start sale_info kvp
                            contents["sale_price"] = int.Parse(retVal[i + 10]);
                            switch (byte.Parse(retVal[i + 11]))
                            {
                                default:
                                    contents["sale_type"] = "not";
                                    break;
                                case 1:
                                    contents["sale_type"] = "original";
                                    break;
                                case 2:
                                    contents["sale_type"] = "copy";
                                    break;
                                case 3:
                                    contents["sale_type"] = "contents";
                                    break;
                            }
                            contents.WriteEndMap();

                            #endregion

                            contents["created_at"] = int.Parse(retVal[i + 12]);
                            contents["flags"] = uint.Parse(retVal[i + 15]);
                            contents["item_id"] = UUID.Parse(retVal[i + 16]);
                            contents["parent_id"] = UUID.Parse(retVal[i + 18]);
                            contents["agent_id"] = avatarID;

                            AssetType assetType = (AssetType)int.Parse(retVal[i + 1]);
                            if (assetType == AssetType.Link)
                            {
                                moreLinkedItems.Add(assetID);
                            }
                            contents["type"] = Utils.AssetTypeToString((AssetType)Util.CheckMeshType((sbyte)assetType));

                            InventoryType invType = (InventoryType)int.Parse(retVal[i + 6]);
                            contents["inv_type"] = Utils.InventoryTypeToString(invType);

                            if (addToCount)
                            {
                                count++;
                            }
                            contents.WriteEndMap(); //end array items

                            #endregion

                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    GD.CloseDatabase();
                }
                if (moreLinkedItems.Count > 0)
                {
                    addToCount = false;
                    filter.andFilters = new Dictionary<string,object>(0);
                    filter.orMultiFilters["inventoryID"] = new List<object>(moreLinkedItems.Count);
                    foreach (UUID item in moreLinkedItems)
                    {
                        filter.orMultiFilters["inventoryID"].Add(item);
                    }
                    moreLinkedItems.Clear();
                    goto redoQuery;
                }
                contents.WriteEndArray( /*"items"*/); //end array items

                contents.WriteStartArray("categories"); //We don't send any folders
                int version = 0;
                filter = new QueryFilter();
                filter.andFilters["folderID"] = folder_id;
                List<string> versionRetVal = GD.Query(new string[]{
                    "version",
                    "type"
                }, m_foldersrealm, filter, null, null, null);

                List<InventoryFolderBase> foldersToAdd = new List<InventoryFolderBase>();
                if (versionRetVal.Count > 0)
                {
                    version = int.Parse(versionRetVal[0]);
                    if (int.Parse(versionRetVal[1]) == (int) AssetType.TrashFolder ||
                        int.Parse(versionRetVal[1]) == (int) AssetType.CurrentOutfitFolder ||
                        int.Parse(versionRetVal[1]) == (int) AssetType.LinkFolder)
                    {
                        //If it is the trash folder, we need to send its descendents, because the viewer wants it

                        filter = new QueryFilter();
                        filter.andFilters["parentFolderID"] = folder_id;
                        filter.andFilters["agentID"] = AgentID;

                        retVal = GD.Query(new string[1] { "*" }, m_foldersrealm, filter, null, null, null);

                        if (retVal.Count % 6 == 0)
                        {
                            try
                            {
                                for (int i = 0; i < retVal.Count; i += 6)
                                {
                                    contents.WriteStartMap("folder");
                                    contents["folder_id"] = UUID.Parse(retVal[i]);
                                    contents["parent_id"] = UUID.Parse(retVal[i + 2]);
                                    contents["name"] = retVal[i + 3];
                                    contents["type"] = int.Parse(retVal[i + 4]);
                                    contents["preferred_type"] = int.Parse(retVal[i + 4]);

                                    count++;
                                    contents.WriteEndMap();
                                }
                            }
                            catch { }
                            finally
                            {
                                GD.CloseDatabase();
                            }
                        }
                    }
                }

                contents.WriteEndArray( /*"categories"*/);
                contents["descendents"] = count;
                contents["version"] = version;

                //Now add it to the folder array
                contents.WriteEndMap(); //end array internalContents
            }

            contents.WriteEndArray(); //end array folders
            contents.WriteEndMap( /*"llsd"*/); //end llsd

            try
            {
                return contents.GetSerializer();
            }
            finally
            {
                contents = null;
            }
        }

        public virtual bool StoreFolder(InventoryFolderBase folder)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["folderID"] = folder.ID;
            GD.Delete(m_foldersrealm, filter);
            Dictionary<string, object> row = new Dictionary<string, object>(6);
            row["folderName"] = folder.Name.MySqlEscape(64);
            row["type"] = folder.Type;
            row["version"] = folder.Version;
            row["folderID"] = folder.ID;
            row["agentID"] = folder.Owner;
            row["parentFolderID"] = folder.ParentID;
            return GD.Insert(m_foldersrealm, row);
        }

        public virtual bool StoreItem(InventoryItemBase item)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["inventoryID"] = item.ID;
            GD.Delete(m_itemsrealm, filter);
            Dictionary<string, object> row = new Dictionary<string, object>(20);
            row["assetID"] = item.AssetID;
            row["assetType"] = item.AssetType;
            row["inventoryName"] = item.Name.MySqlEscape(64);
            row["inventoryDescription"] = item.Description.MySqlEscape(128);
            row["inventoryNextPermissions"] = item.NextPermissions;
            row["inventoryCurrentPermissions"] = item.CurrentPermissions;
            row["invType"] = item.InvType;
            row["creatorID"] = item.CreatorIdentification;
            row["inventoryBasePermissions"] = item.BasePermissions;
            row["inventoryEveryOnePermissions"] = item.EveryOnePermissions;
            row["salePrice"] = item.SalePrice;
            row["saleType"] = item.SaleType;
            row["creationDate"] = item.CreationDate;
            row["groupID"] = item.GroupID;
            row["groupOwned"] = item.GroupOwned ? "1" : "0";
            row["flags"] = item.Flags;
            row["inventoryID"] = item.ID;
            row["avatarID"] = item.Owner;
            row["parentFolderID"] = item.Folder;
            row["inventoryGroupPermissions"] = item.GroupPermissions;
            return GD.Insert(m_itemsrealm, row);
        }

        public virtual bool UpdateAssetIDForItem(UUID itemID, UUID assetID)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(1);
            values["assetID"] = assetID;

            QueryFilter filter = new QueryFilter();
            filter.andFilters["inventoryID"] = itemID;

            return GD.Update(m_itemsrealm, values, null, filter, null, null);
        }

        public virtual bool DeleteFolders(string field, string val, bool safe)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters[field] = val;
            if (safe)
            {
                filter.andFilters["type"] = "-1";
            }
            return GD.Delete(m_foldersrealm, filter);
        }

        public virtual bool DeleteItems(string field, string val)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters[field] = val;
            return GD.Delete(m_itemsrealm, filter);
        }

        public virtual bool MoveItem(string id, string newParent)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(1);
            values["parentFolderID"] = newParent;

            QueryFilter filter = new QueryFilter();
            filter.andFilters["inventoryID"] = id;

            return GD.Update(m_itemsrealm, values, null, filter, null, null);
        }

        public virtual void IncrementFolder(UUID folderID)
        {
            Dictionary<string, int> incrementValues = new Dictionary<string, int>(1);
            incrementValues["version"] = 1;

            QueryFilter filter = new QueryFilter();
            filter.andFilters["folderID"] = folderID.ToString();

            GD.Update(m_foldersrealm, new Dictionary<string, object>(0), incrementValues, filter, null, null);
        }

        public virtual void IncrementFolderByItem(UUID itemID)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["inventoryID"] = itemID;
            List<string> values = GD.Query(new string[] { "parentFolderID" }, m_itemsrealm, filter, null, null, null);
            if (values.Count > 0)
            {
                IncrementFolder(UUID.Parse(values[0]));
            }
        }

        public virtual InventoryItemBase[] GetActiveGestures(UUID principalID)
        {
            QueryFilter filter = new QueryFilter();
            filter.andFilters["avatarID"] = principalID;
            filter.andFilters["assetType"] = (int)AssetType.Gesture;
            filter.andBitfieldAndFilters["flags"] = 1;

            return ParseInventoryItems(GD.Query(new string[1] { "*" }, m_itemsrealm, filter, null, null, null)).ToArray();
        }

        #endregion

        public void Dispose()
        {
        }

        private OSDArray ParseLLSDInventoryItems(List<string> retVal)
        {
            OSDArray resp = new OSDArray();
            OSDMap item;
            OSDMap permissions;
            OSDMap sale_info;
            List<InventoryItemBase> items = ParseInventoryItems(retVal);

            foreach (InventoryItemBase inventoryItem in items)
            {
                item = new OSDMap();
                permissions = new OSDMap();
                sale_info = new OSDMap();

                permissions["next_owner_mask"] = inventoryItem.NextPermissions;
                permissions["owner_mask"] = inventoryItem.CurrentPermissions;
                permissions["creator_id"] = inventoryItem.CreatorIdAsUuid;
                permissions["base_mask"] = inventoryItem.BasePermissions;
                permissions["everyone_mask"] = inventoryItem.EveryOnePermissions;
                permissions["group_id"] = inventoryItem.GroupID;
                permissions["is_owner_group"] = inventoryItem.GroupOwned;
                permissions["group_mask"] = inventoryItem.GroupPermissions;
                permissions["owner_id"] = inventoryItem.Owner;
                permissions["last_owner_id"] = inventoryItem.Owner;

                sale_info["sale_priec"] = inventoryItem.SalePrice;
                switch (inventoryItem.SaleType)
                {
                    default:
                        sale_info["sale_type"] = "not";
                        break;
                    case 1:
                        sale_info["sale_type"] = "original";
                        break;
                    case 2:
                        sale_info["sale_type"] = "copy";
                        break;
                    case 3:
                        sale_info["sale_type"] = "contents";
                        break;
                }

                item["asset_id"] = inventoryItem.AssetID;
                item["name"] = inventoryItem.Name;
                item["desc"] = inventoryItem.Description;
                item["created_at"] = inventoryItem.CreationDate;
                item["flags"] = inventoryItem.Flags;
                item["item_id"] = inventoryItem.ID;
                item["parent_id"] = inventoryItem.Folder;
                item["agent_id"] = inventoryItem.Owner;
                item["type"] = Utils.AssetTypeToString((AssetType)inventoryItem.AssetType);
                item["inv_type"] = Utils.InventoryTypeToString((InventoryType)inventoryItem.InvType);
                item["sale_info"] = sale_info;
                item["permissions"] = permissions;

                resp.Add(item);
            }

            return resp;
        }

        private OSDArray ParseLLSDInventoryItems(IDataReader retVal)
        {
            OSDArray array = new OSDArray();

            while (retVal.Read())
            {
                OSDMap item = new OSDMap();
                OSDMap permissions = new OSDMap();
                item["asset_id"] = UUID.Parse(retVal["assetID"].ToString());
                item["name"] = retVal["inventoryName"].ToString();
                item["desc"] = retVal["inventoryDescription"].ToString();
                permissions["next_owner_mask"] = uint.Parse(retVal["inventoryNextPermissions"].ToString());
                permissions["owner_mask"] = uint.Parse(retVal["inventoryCurrentPermissions"].ToString());
                UUID creator;
                if (UUID.TryParse(retVal["creatorID"].ToString(), out creator))
                    permissions["creator_id"] = creator;
                else
                    permissions["creator_id"] = UUID.Zero;
                permissions["base_mask"] = uint.Parse(retVal["inventoryBasePermissions"].ToString());
                permissions["everyone_mask"] = uint.Parse(retVal["inventoryEveryOnePermissions"].ToString());
                OSDMap sale_info = new OSDMap();
                sale_info["sale_price"] = int.Parse(retVal["salePrice"].ToString());
                switch (byte.Parse(retVal["saleType"].ToString()))
                {
                    default:
                        sale_info["sale_type"] = "not";
                        break;
                    case 1:
                        sale_info["sale_type"] = "original";
                        break;
                    case 2:
                        sale_info["sale_type"] = "copy";
                        break;
                    case 3:
                        sale_info["sale_type"] = "contents";
                        break;
                }
                item["sale_info"] = sale_info;
                item["created_at"] = int.Parse(retVal["creationDate"].ToString());
                permissions["group_id"] = UUID.Parse(retVal["groupID"].ToString());
                permissions["is_owner_group"] = int.Parse(retVal["groupOwned"].ToString()) == 1;
                item["flags"] = uint.Parse(retVal["flags"].ToString());
                item["item_id"] = UUID.Parse(retVal["inventoryID"].ToString());
                item["parent_id"] = UUID.Parse(retVal["parentFolderID"].ToString());
                permissions["group_mask"] = uint.Parse(retVal["inventoryGroupPermissions"].ToString());
                item["agent_id"] = UUID.Parse(retVal["avatarID"].ToString());
                permissions["owner_id"] = item["agent_id"];
                permissions["last_owner_id"] = item["agent_id"];

                item["type"] = Utils.AssetTypeToString((AssetType) int.Parse(retVal["assetType"].ToString()));
                item["inv_type"] = Utils.InventoryTypeToString((InventoryType) int.Parse(retVal["invType"].ToString()));

                item["permissions"] = permissions;

                array.Add(item);
            }
            //retVal.Close();

            return array;
        }

        private List<InventoryFolderBase> ParseInventoryFolders(List<string> retVal)
        {
            List<InventoryFolderBase> folders = new List<InventoryFolderBase>();
            if (retVal.Count % 6 != 0)
            {
                return folders;
            }
            for (int i = 0; i < retVal.Count; i += 6)
            {
                InventoryFolderBase folder = new InventoryFolderBase
                {
                    ID = UUID.Parse(retVal[i]),
                    Owner = UUID.Parse(retVal[i + 1]),
                    ParentID = UUID.Parse(retVal[i + 2]),
                    Name = retVal[i + 3],
                    Type = short.Parse(retVal[i + 4]),
                    Version = (ushort)int.Parse(retVal[i + 5])
                };
                folders.Add(folder);
            }
            //retVal.Clear();
            return folders;
        }

        private List<InventoryItemBase> ParseInventoryItems(List<string> retVal)
        {
            List<InventoryItemBase> items = new List<InventoryItemBase>();

            if (retVal.Count % 20 == 0)
            {
                for (int i = 0; i < retVal.Count; i += 20)
                {
                    items.Add(new InventoryItemBase
                    {
                        AssetID = UUID.Parse(retVal[i]),
                        AssetType = int.Parse(retVal[i + 1]),
                        Name = retVal[i + 2],
                        Description = retVal[i + 3],
                        NextPermissions = uint.Parse(retVal[i + 4]),
                        CurrentPermissions = uint.Parse(retVal[i + 5]),
                        InvType = int.Parse(retVal[i + 6]),
                        CreatorIdentification = retVal[i + 7],
                        BasePermissions = uint.Parse(retVal[i + 8]),
                        EveryOnePermissions = uint.Parse(retVal[i + 9]),
                        SalePrice = int.Parse(retVal[i + 10]),
                        SaleType = byte.Parse(retVal[i + 11]),
                        CreationDate = int.Parse(retVal[i + 12]),
                        GroupID = UUID.Parse(retVal[i + 13]),
                        GroupOwned = int.Parse(retVal[i + 14]) == 1,
                        Flags = uint.Parse(retVal[i + 15]),
                        ID = UUID.Parse(retVal[i + 16]),
                        Owner = UUID.Parse(retVal[i + 17]),
                        Folder = UUID.Parse(retVal[i + 18]),
                        GroupPermissions = uint.Parse(retVal[i + 19])
                    });
                }
            }

            return items;
        }

        #region Nested type: LLSDSerializationDictionary

        public class LLSDSerializationDictionary
        {
            private readonly MemoryStream sw = new MemoryStream();
            private readonly XmlTextWriter writer;

            public LLSDSerializationDictionary()
            {
                writer = new XmlTextWriter(sw, Encoding.UTF8);
                writer.WriteStartElement(String.Empty, "llsd", String.Empty);
            }

            public object this[string name]
            {
                set
                {
                    writer.WriteStartElement(String.Empty, "key", String.Empty);
                    writer.WriteString(name);
                    writer.WriteEndElement();
                    Type t = value.GetType();
                    if (t == typeof (bool))
                    {
                        writer.WriteStartElement(String.Empty, "boolean", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (int))
                    {
                        writer.WriteStartElement(String.Empty, "integer", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (uint))
                    {
                        writer.WriteStartElement(String.Empty, "integer", String.Empty);
                        writer.WriteValue(value.ToString());
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (float))
                    {
                        writer.WriteStartElement(String.Empty, "real", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (double))
                    {
                        writer.WriteStartElement(String.Empty, "real", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (string))
                    {
                        writer.WriteStartElement(String.Empty, "string", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (UUID))
                    {
                        writer.WriteStartElement(String.Empty, "uuid", String.Empty);
                        writer.WriteValue(value.ToString()); //UUID has to be string!
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (DateTime))
                    {
                        writer.WriteStartElement(String.Empty, "date", String.Empty);
                        writer.WriteValue(AsString((DateTime) value));
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (Uri))
                    {
                        writer.WriteStartElement(String.Empty, "uri", String.Empty);
                        writer.WriteValue((value).ToString()); //URI has to be string
                        writer.WriteEndElement();
                    }
                    else if (t == typeof (byte[]))
                    {
                        writer.WriteStartElement(String.Empty, "binary", String.Empty);
                        writer.WriteStartAttribute(String.Empty, "encoding", String.Empty);
                        writer.WriteString("base64");
                        writer.WriteEndAttribute();
                        writer.WriteValue(Convert.ToBase64String((byte[]) value)); //Has to be base64
                        writer.WriteEndElement();
                    }
                    t = null;
                }
            }

            public void WriteStartMap(string name)
            {
                writer.WriteStartElement(String.Empty, "map", String.Empty);
            }

            public void WriteEndMap()
            {
                writer.WriteEndElement();
            }

            public void WriteStartArray(string name)
            {
                writer.WriteStartElement(String.Empty, "array", String.Empty);
            }

            public void WriteEndArray()
            {
                writer.WriteEndElement();
            }

            public void WriteKey(string key)
            {
                writer.WriteStartElement(String.Empty, "key", String.Empty);
                writer.WriteString(key);
                writer.WriteEndElement();
            }

            public void WriteElement(object value)
            {
                Type t = value.GetType();
                if (t == typeof (bool))
                {
                    writer.WriteStartElement(String.Empty, "boolean", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof (int))
                {
                    writer.WriteStartElement(String.Empty, "integer", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof (uint))
                {
                    writer.WriteStartElement(String.Empty, "integer", String.Empty);
                    writer.WriteValue(value.ToString());
                    writer.WriteEndElement();
                }
                else if (t == typeof (float))
                {
                    writer.WriteStartElement(String.Empty, "real", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof (double))
                {
                    writer.WriteStartElement(String.Empty, "real", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof (string))
                {
                    writer.WriteStartElement(String.Empty, "string", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof (UUID))
                {
                    writer.WriteStartElement(String.Empty, "uuid", String.Empty);
                    writer.WriteValue(value.ToString()); //UUID has to be string!
                    writer.WriteEndElement();
                }
                else if (t == typeof (DateTime))
                {
                    writer.WriteStartElement(String.Empty, "date", String.Empty);
                    writer.WriteValue(AsString((DateTime) value));
                    writer.WriteEndElement();
                }
                else if (t == typeof (Uri))
                {
                    writer.WriteStartElement(String.Empty, "uri", String.Empty);
                    writer.WriteValue((value).ToString()); //URI has to be string
                    writer.WriteEndElement();
                }
                else if (t == typeof (byte[]))
                {
                    writer.WriteStartElement(String.Empty, "binary", String.Empty);
                    writer.WriteStartAttribute(String.Empty, "encoding", String.Empty);
                    writer.WriteString("base64");
                    writer.WriteEndAttribute();
                    writer.WriteValue(Convert.ToBase64String((byte[]) value)); //Has to be base64
                    writer.WriteEndElement();
                }
                t = null;
            }

            public byte[] GetSerializer()
            {
                writer.WriteEndElement();
                writer.Close();

                byte[] array = sw.ToArray();
                /*byte[] newarr = new byte[array.Length - 3];
                Array.Copy(array, 3, newarr, 0, newarr.Length);
                writer = null;
                sw = null;
                array = null;*/
                return array;
            }

            private string AsString(DateTime value)
            {
                string format;
                format = value.Millisecond > 0 ? "yyyy-MM-ddTHH:mm:ss.ffZ" : "yyyy-MM-ddTHH:mm:ssZ";
                return value.ToUniversalTime().ToString(format);
            }
        }

        #endregion
    }
}