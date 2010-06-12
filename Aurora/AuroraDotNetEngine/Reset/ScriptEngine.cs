/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Threading;
using System.Xml;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.Framework.EventQueue;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.Shared.CodeTools;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Framework.Console;
using Amib.Threading;

namespace Aurora.ScriptEngine.AuroraDotNetEngine
{
    [Serializable]
    public class ScriptEngine : INonSharedRegionModule, IScriptEngine, IScriptModule
    {
        #region Declares 

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_Scene;
        public Scene World
        {
            get { return m_Scene; }
        }

        // Handles and queues incoming events from OpenSim
        public static EventManager EventManager;

        // Handles loading/unloading of scripts into AppDomains
        public static AppDomainManager AppDomainManager;

        //The compiler for all scripts
        public static Compiler LSLCompiler;

        //Queue that handles the loading and unloading of scripts
        public static OpenSim.Framework.LocklessQueue<LUStruct> LUQueue = new OpenSim.Framework.LocklessQueue<LUStruct>();

        public static MaintenanceThread m_MaintenanceThread;

        private IConfigSource m_ConfigSource;
        public IConfig ScriptConfigSource;
        private bool m_enabled = false;

        public IConfig Config
        {
            get { return ScriptConfigSource; }
        }

        public IConfigSource ConfigSource
        {
            get { return m_ConfigSource; }
        }

        public string ScriptEngineName
        {
            get { return "AuroraDotNetEngineReset"; }
        }
        
        public IScriptModule ScriptModule
        {
            get { return this; }
        }

        public event ScriptRemoved OnScriptRemoved;
        public event ObjectRemoved OnObjectRemoved;
        private IXmlRpcRouter m_XmlRpcRouter;
        public IScriptProtectionModule ScriptProtection;
        
        /// <summary>
        /// Removes the script from the event queue so it does not fire anymore events.
        /// </summary>
        public static Dictionary<UUID, uint> NeedsRemoved = new Dictionary<UUID, uint>();

        /// <summary>
        /// Queue containing events waiting to be executed.
        /// </summary>
        public static OpenSim.Framework.LocklessQueue<QueueItemStruct> EventQueue = new OpenSim.Framework.LocklessQueue<QueueItemStruct>();

        /// <summary>
        /// Queue containing scripts that need to have states saved or deleted.
        /// </summary>
        public static OpenSim.Framework.LocklessQueue<StateQueueItem> StateQueue = new OpenSim.Framework.LocklessQueue<StateQueueItem>();
        
        /// <summary>
        /// Maximum events in the event queue at any one time
        /// </summary>
        public int EventExecutionMaxQueueSize;

        /// <summary>
        /// Time for each of the smart thread pool threads to sleep before doing the queues.
        /// </summary>
        public int SleepTime;

        /// <summary>
        /// Number of threads that should be running  to deal with starting and stopping scripts
        /// </summary>
        public int NumberOfStartStopThreads;

        /// <summary>
        /// Number of threads that should be running to save the states
        /// </summary>
        public int NumberOfStateSavingThreads;

        /// <summary>
        /// Number of Event Queue threads that should be running
        /// </summary>
        public int NumberOfEventQueueThreads;

        public System.Timers.Timer UpdateLeasesTimer = null;

        #endregion

        #region Constructor and Shutdown
        
        public void Shutdown()
        {
            // We are shutting down
            foreach (ScriptData ID in ScriptProtection.GetAllScripts())
            {
                try
                {
                    ID.CloseAndDispose(false);
                }
                catch (Exception) { }
            }
        }

        #endregion

        #region INonSharedRegionModule

        public void Initialise(IConfigSource config)
        {
            m_ConfigSource = config;
            ScriptConfigSource = config.Configs[ScriptEngineName];
            if (ScriptConfigSource == null)
                return;

            NumberOfEventQueueThreads = ScriptConfigSource.GetInt("NumberOfEventQueueThreads", 5);
            NumberOfStateSavingThreads = ScriptConfigSource.GetInt("NumberOfStateSavingThreads", 1);
            NumberOfStartStopThreads = ScriptConfigSource.GetInt("NumberOfStartStopThreads", 1);
            SleepTime = ScriptConfigSource.GetInt("SleepTime", 50);
            LoadUnloadMaxQueueSize = ScriptConfigSource.GetInt("LoadUnloadMaxQueueSize", 100);
            EventExecutionMaxQueueSize = ScriptConfigSource.GetInt("EventExecutionMaxQueueSize", 300);
        }

        public void PostInitialise() { }
        public void AddRegion(Scene scene)
        {
            // Make sure we have config
            if (ConfigSource.Configs[ScriptEngineName] == null)
                ConfigSource.AddConfig(ScriptEngineName);

            m_enabled = ScriptConfigSource.GetBoolean("Enabled", true);
            if (!m_enabled)
                return;

            //Register the console commands
            if (m_Scene == null)
            {
                scene.AddCommand(this, "DotNet restart all scripts", "DotNet restart all scripts", "Restarts all scripts in the sim", RestartAllScripts);
                scene.AddCommand(this, "DotNet stop all scripts", "DotNet stop all scripts", "Stops all scripts in the sim", StopAllScripts);
                scene.AddCommand(this, "DotNet start all scripts", "DotNet start all scripts", "Restarts all scripts in the sim", StartAllScripts);
            }
            
            ScriptConfigSource = ConfigSource.Configs[ScriptEngineName];

        	//m_log.Info("[" + ScriptEngineName + "]: ScriptEngine initializing");
            m_Scene = scene;

            // Create all objects we'll be using
            if(ScriptProtection == null)
                ScriptProtection = (IScriptProtectionModule)new ScriptProtectionModule(m_ConfigSource, this);
            
            EventManager = new EventManager(this, true);
            
            // We need to start it
            if (LSLCompiler == null)
                LSLCompiler = new Compiler(this);

            if(AppDomainManager == null)
                AppDomainManager = new AppDomainManager(this);
            
            if(m_MaintenanceThread == null)
                m_MaintenanceThread = new MaintenanceThread(this);

            //m_log.Info("[" + ScriptEngineName + "]: Reading configuration "+
            //        "from config section \"" + ScriptEngineName + "\"");

            scene.StackModuleInterface<IScriptModule>(this);

            m_XmlRpcRouter = m_Scene.RequestModuleInterface<IXmlRpcRouter>();
            if (m_XmlRpcRouter != null)
            {
                OnScriptRemoved += m_XmlRpcRouter.ScriptRemoved;
                OnObjectRemoved += m_XmlRpcRouter.ObjectRemoved;
            }

            scene.EventManager.OnRezScript += OnRezScript;
            scene.EventManager.OnRezScripts += OnRezScripts;
        }

        #region Console Commands

        protected void RestartAllScripts(string module, string[] cmdparams)
        {
            string go = MainConsole.Instance.CmdPrompt("Are you sure you want to restart all scripts? (This also wipes the script state saves database, which could cause loss of information in your scripts)", "no");
            if (go == "yes" || go == "Yes")
            {
                foreach (ScriptData ID in ScriptProtection.GetAllScripts())
                {
                    try
                    {
                        ScriptProtection.RemovePreviouslyCompiled(ID.Source);
                        ID.Start(false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else
            {
                m_log.Info("Not restarting all scripts");
            }
        }

        protected void StartAllScripts(string module, string[] cmdparams)
        {
            string go = MainConsole.Instance.CmdPrompt("Are you sure you want to restart all scripts?", "no");
            if (go == "yes" || go == "Yes")
            {
                foreach (ScriptData ID in ScriptProtection.GetAllScripts())
                {
                    try
                    {
                        ID.Start(true);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else
            {
                m_log.Info("Not restarting all scripts");
            }
        }

        protected void StopAllScripts(string module, string[] cmdparams)
        {
            string go = MainConsole.Instance.CmdPrompt("Are you sure you want to stop all scripts?", "no");
            if (go.Contains("yes") || go.Contains("Yes"))
            {
                StopAllScripts();
            }
            else
            {
                m_log.Info("Not restarting all scripts");
            }
        }

        public void StopAllScripts()
        {
            foreach (ScriptData ID in ScriptProtection.GetAllScripts())
            {
                try
                {
                    ScriptProtection.RemovePreviouslyCompiled(ID.Source);
                    ID.CloseAndDispose(false);
                }
                catch (Exception)
                {
                }
            }
        }

        #endregion

        public void RemoveRegion(Scene scene)
        {
            scene.EventManager.OnScriptReset -= OnScriptReset;
            scene.EventManager.OnGetScriptRunning -= OnGetScriptRunning;
            scene.EventManager.OnStartScript -= OnStartScript;
            scene.EventManager.OnStopScript -= OnStopScript;
            
            if (m_XmlRpcRouter != null)
            {
                OnScriptRemoved -= m_XmlRpcRouter.ScriptRemoved;
                OnObjectRemoved -= m_XmlRpcRouter.ObjectRemoved;
            }

            scene.UnregisterModuleInterface<IScriptModule>(this);

            Shutdown();
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;
            EventManager.HookUpEvents();

            scene.EventManager.OnScriptReset += OnScriptReset;
            scene.EventManager.OnGetScriptRunning += OnGetScriptRunning;
            scene.EventManager.OnStartScript += OnStartScript;
            scene.EventManager.OnStopScript += OnStopScript;
            UpdateLeasesTimer = new System.Timers.Timer(9.5 * 1000 * 60 /*9.5 minutes*/);
            UpdateLeasesTimer.Enabled = true;
            UpdateLeasesTimer.Elapsed += UpdateAllLeases;
            UpdateLeasesTimer.Start();
        }

        public void UpdateAllLeases(object sender, System.Timers.ElapsedEventArgs e)
        {
            foreach (ScriptData script in ScriptProtection.GetAllScripts())
            {
                if (script.Running == false || script.Disabled == true)
                    return;

                try
                {
                    ILease lease = (ILease)RemotingServices.GetLifetimeService(script.Script as ScriptBaseClass);
                    lease.Renew(DateTime.Now.AddMinutes(10) - DateTime.Now);
                }
                catch (Exception ex)
                {
                    m_log.Error("Lease found dead!" + script.ItemID);
                }
            }
        }

        public void Close()
        {
        }

        public Type ReplaceableInterface 
        {
            get { return null; }
        }

        public string Name
        {
            get { return ScriptEngineName; }
        }
        
		#endregion

        public void OnRezScript(uint localID, UUID itemID, string script,
                int startParam, bool postOnRez, string engine, int stateSource)
        {
            if (script.StartsWith("//MRM:"))
                return;

            List<IScriptModule> engines =
                new List<IScriptModule>(
                World.RequestModuleInterfaces<IScriptModule>());

            List<string> names = new List<string>();
            foreach (IScriptModule m in engines)
                names.Add(m.ScriptEngineName);

            int lineEnd = script.IndexOf('\n');

            if (lineEnd > 1)
            {
                string firstline = script.Substring(0, lineEnd).Trim();

                int colon = firstline.IndexOf(':');
                if (firstline.Length > 2 &&
                    firstline.Substring(0, 2) == "//" && colon != -1)
                {
                    string engineName = firstline.Substring(2, colon - 2);

                    if (names.Contains(engineName))
                    {
                        engine = engineName;
                        script = "//" + script.Substring(script.IndexOf(':') + 1);
                    }
                    else
                    {
                        if (engine == ScriptEngineName)
                        {
                            SceneObjectPart part =
                                    World.GetSceneObjectPart(
                                    localID);

                            TaskInventoryItem item =
                                    part.Inventory.GetInventoryItem(itemID);

                            ScenePresence presence =
                                    World.GetScenePresence(
                                    item.OwnerID);

                            if (presence != null)
                            {
                                presence.ControllingClient.SendAgentAlertMessage(
                                         "Selected engine unavailable. " +
                                         "Running script on " +
                                         ScriptEngineName,
                                         false);
                            }
                        }
                    }
                }
            }

            if (engine != ScriptEngineName)
                return;

            StartScript(localID, itemID, script,
                    startParam, postOnRez, (StateSource)stateSource);
        }

        public void OnRezScripts(uint localID, TaskInventoryItem[] items,
                int startParam, bool postOnRez, string engine, int stateSource)
        {
            List<TaskInventoryItem> ItemsToStart = new List<TaskInventoryItem>();
            foreach (TaskInventoryItem item in items)
            {
                AssetBase asset = m_Scene.AssetService.Get(item.AssetID.ToString());
                if (null == asset)
                {
                    m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Couldn't start script {0}, {1} since asset ID {4} could not be found",
                        item.Name, item.ItemID, item.AssetID);
                    continue;
                }
                string script = Utils.BytesToString(asset.Data);

                if (script.StartsWith("//MRM:"))
                    return;

                List<IScriptModule> engines =
                    new List<IScriptModule>(
                    World.RequestModuleInterfaces<IScriptModule>());

                List<string> names = new List<string>();
                foreach (IScriptModule m in engines)
                    names.Add(m.ScriptEngineName);

                int lineEnd = script.IndexOf('\n');

                if (lineEnd > 1)
                {
                    string firstline = script.Substring(0, lineEnd).Trim();

                    int colon = firstline.IndexOf(':');
                    if (firstline.Length > 2 &&
                        firstline.Substring(0, 2) == "//" && colon != -1)
                    {
                        string engineName = firstline.Substring(2, colon - 2);

                        if (names.Contains(engineName))
                        {
                            engine = engineName;
                            script = "//" + script.Substring(script.IndexOf(':') + 1);
                        }
                        else
                        {
                            if (engine == ScriptEngineName)
                            {
                                SceneObjectPart part =
                                        World.GetSceneObjectPart(
                                        localID);

                                ScenePresence presence =
                                        World.GetScenePresence(
                                        item.OwnerID);

                                if (presence != null)
                                {
                                    presence.ControllingClient.SendAgentAlertMessage(
                                             "Selected engine unavailable. " +
                                             "Running script on " +
                                             ScriptEngineName,
                                             false);
                                }
                            }
                        }
                    }
                }

                if (engine != ScriptEngineName)
                    return;
                ItemsToStart.Add(item);
            }

            foreach (TaskInventoryItem item in ItemsToStart)
            {
                AssetBase asset = m_Scene.AssetService.Get(item.AssetID.ToString());
                if (null == asset)
                {
                    m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Couldn't start script {0}, {1} since asset ID {4} could not be found",
                        item.Name, item.ItemID, item.AssetID);
                    continue;
                }
                string script = Utils.BytesToString(asset.Data);

                StartScript(localID, item.ItemID, script,
                        startParam, postOnRez, (StateSource)stateSource);
            }
        }

		#region Post Object Events
		
        public bool PostObjectEvent(uint localID, EventParams p)
        {
            return AddToObjectQueue(localID, p.EventName,
                    p.DetectParams, p.Params);
        }

        public bool PostScriptEvent(UUID itemID, EventParams p)
        {
            ScriptData ID = GetScriptByItemID(itemID);
            return AddToScriptQueue(ID,
                    p.EventName, p.DetectParams, p.Params);
        }

        public bool PostScriptEvent(UUID itemID, string name, Object[] p)
        {
            Object[] lsl_p = new Object[p.Length];
            for (int i = 0; i < p.Length ; i++)
            {
                if (p[i] is int)
                    lsl_p[i] = new LSL_Types.LSLInteger((int)p[i]);
                else if (p[i] is string)
                    lsl_p[i] = new LSL_Types.LSLString((string)p[i]);
                else if (p[i] is Vector3)
                    lsl_p[i] = new LSL_Types.Vector3(((Vector3)p[i]).X, ((Vector3)p[i]).Y, ((Vector3)p[i]).Z);
                else if (p[i] is Quaternion)
                    lsl_p[i] = new LSL_Types.Quaternion(((Quaternion)p[i]).X, ((Quaternion)p[i]).Y, ((Quaternion)p[i]).Z, ((Quaternion)p[i]).W);
                else if (p[i] is float)
                    lsl_p[i] = new LSL_Types.LSLFloat((float)p[i]);
                else
                    lsl_p[i] = p[i];
            }

            return PostScriptEvent(itemID, new EventParams(name, lsl_p, new DetectParams[0]));
        }

        public bool PostObjectEvent(UUID itemID, string name, Object[] p)
        {
            SceneObjectPart part = m_Scene.GetSceneObjectPart(itemID);
            if (part == null)
                return false;

            Object[] lsl_p = new Object[p.Length];
            for (int i = 0; i < p.Length ; i++)
            {
                if (p[i] is int)
                    lsl_p[i] = new LSL_Types.LSLInteger((int)p[i]);
                else if (p[i] is string)
                    lsl_p[i] = new LSL_Types.LSLString((string)p[i]);
                else if (p[i] is Vector3)
                    lsl_p[i] = new LSL_Types.Vector3(((Vector3)p[i]).X, ((Vector3)p[i]).Y, ((Vector3)p[i]).Z);
                else if (p[i] is Quaternion)
                    lsl_p[i] = new LSL_Types.Quaternion(((Quaternion)p[i]).X, ((Quaternion)p[i]).Y, ((Quaternion)p[i]).Z, ((Quaternion)p[i]).W);
                else if (p[i] is float)
                    lsl_p[i] = new LSL_Types.LSLFloat((float)p[i]);
                else
                    lsl_p[i] = p[i];
            }

            return PostObjectEvent(part.LocalId, new EventParams(name, lsl_p, new DetectParams[0]));
        }

        public DetectParams GetDetectParams(UUID itemID, int number)
        {
            ScriptData id = GetScriptByItemID(itemID);

            if (id == null)
                return null;

            DetectParams[] det = id.LastDetectParams;

            if (number < 0 || number >= det.Length)
                return null;

            return det[number];
        }

        #endregion
        
        #region Get/Set Start Parameter and Min Event Delay
        
        public int GetStartParameter(UUID itemID)
        {
            ScriptData id = GetScriptByItemID(itemID);

            if (id == null)
                return 0;

            return id.StartParam;
        }

        public void SetMinEventDelay(UUID itemID, double delay)
        {
            ScriptData ID = GetScriptByItemID(itemID);
            if(ID == null)
            {
                m_log.ErrorFormat("[{0}]: SetMinEventDelay found no InstanceData for script {1}.",ScriptEngineName,itemID.ToString());
                return;
            }
            ID.EventDelayTicks = (long)delay;
        }

        #endregion

        #region Get/Set Script States/Running

        public void SetState(UUID itemID, string state)
        {
            ScriptData id = GetScriptByItemID(itemID);

            if (id == null)
                return;

            if (id.State != state)
            {
                PostObjectEvent(id.localID, new EventParams(
                    "state_exit", new object[0] { },
                    new DetectParams[0]));
                id.State = state;
                int eventFlags = id.Script.GetStateEventFlags(id.State);
                
            	id.part.SetScriptEvents(itemID, eventFlags);
                UpdateScriptInstanceData(id);

                PostObjectEvent(id.localID, new EventParams(
                    "state_entry", new object[] { },
                    new DetectParams[0]));
            }
        }

        public bool GetScriptState(UUID itemID)
        {
            ScriptData id = GetScriptByItemID(itemID);
            if (id == null)
                return false;

            return id.Running;
        }

        public void SetScriptState(UUID itemID, bool state)
        {
            ScriptData id = GetScriptByItemID(itemID);
            if (id == null)
                return;

            if (!id.Disabled)
                id.Running = state;
        }

        #endregion

        #region Reset

        public void ApiResetScript(UUID itemID)
        {
            ScriptData ID = GetScriptByItemID(itemID);
            if (ID == null)
                return;

            ID.Reset();
        }

        public void ResetScript(UUID itemID)
        {
            ScriptData ID = GetScriptByItemID(itemID);
            if (ID == null)
                return;

            ID.Reset();
        }

        public void OnScriptReset(uint localID, UUID itemID)
        {
            ScriptData ID = GetScript(localID, itemID);
            if (ID == null)
                return;

            ID.Reset();
        }
        #endregion

        #region Start/End/Suspend Scripts

        public void OnStartScript(uint localID, UUID itemID)
        {
            ScriptData id = GetScript(localID, itemID);
            if (id == null)
                return;        

            if (!id.Disabled)
                id.Running = true;
            StartScript(localID, itemID, id.Source, id.StartParam, true, id.stateSource);
        }

        public void OnStopScript(uint localID, UUID itemID)
        {
            ScriptData id = GetScript(localID, itemID);
            if (id == null)
                return;        
            
            StopScript(localID, itemID);
        }

        public void OnGetScriptRunning(IClientAPI controllingClient,
                UUID objectID, UUID itemID)
        {
            ScriptData id = GetScriptByItemID(itemID);
            if (id == null)
                return;        

            IEventQueue eq = World.RequestModuleInterface<IEventQueue>();
            if (eq == null)
            {
                controllingClient.SendScriptRunningReply(objectID, itemID,
                        id.Running);
            }
            else
            {
                eq.Enqueue(EventQueueHelper.ScriptRunningReplyEvent(objectID, itemID, id.Running, true),
                           controllingClient.AgentId);
            }
        }

        public void SuspendScript(UUID itemID)
        {
            ScriptData ID = GetScriptByItemID(itemID);
            if (ID == null)
                return;
            ID.Suspended = true;
        }

        public void ResumeScript(UUID itemID)
        {
            ScriptData ID = GetScriptByItemID(itemID);
            if (ID == null)
                m_MaintenanceThread.AddResumeScript(itemID);
            else
            {
                ID.Suspended = false;
                m_MaintenanceThread.RemoveResumeScript(itemID);
            }
        }

        #endregion

        #region GetScriptAPI

        public IScriptApi GetApi(UUID itemID, string name)
        {
            ScriptData id = GetScriptByItemID(itemID);
            if (id == null)
                return null;

            if (id.Apis.ContainsKey(name))
                return id.Apis[name];

            return null;
        }

        #endregion

        #region xEngine only

        /// <summary>
        /// Unneeded for DotNet. Only for xEngine.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        public IScriptWorkItem QueueEventHandler(Object o)
        {
            return null;
        }

        #endregion

        #region XML Serialization

        public string GetXMLState(UUID itemID)
        {
            return "";
        }

        public ArrayList GetScriptErrors(UUID itemID)
        {
            return new ArrayList(GetErrors(itemID));
        }

        public bool SetXMLState(UUID itemID, string xml)
        {
            return true;
        }
        #endregion

        /// <summary>
        /// Posts event to all objects in the group.
        /// </summary>
        /// <param name="localID">Region object ID</param>
        /// <param name="FunctionName">Name of the function, will be state + "_event_" + FunctionName</param>
        /// <param name="param">Array of parameters to match event mask</param>
        public bool AddToObjectQueue(uint localID, string FunctionName, DetectParams[] qParams, params object[] param)
        {
            // Determine all scripts in Object and add to their queue
            IScriptData[] datas = ScriptProtection.GetScript(localID);

            if (datas == null)
                //No scripts to post to... so it is firing all the events it needs to
                return true;

            foreach (IScriptData ID in datas)
            {
                // Add to each script in that object
                AddToScriptQueue((ScriptData)ID, FunctionName, qParams, param);
            }
            return true;
        }

        /// <summary>
        /// Posts the event to the given object.
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="FunctionName"></param>
        /// <param name="qParams"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public bool AddToScriptQueue(ScriptData ID, string FunctionName, DetectParams[] qParams, params object[] param)
        {
            lock (EventQueue)
            {
                if (EventQueue.Count >= EventExecutionMaxQueueSize)
                {
                    m_log.WarnFormat("[{0}]: Event Queue is above the MaxQueueSize.", ScriptEngineName);
                    return false;
                }
                if (FunctionName == "timer")
                {
                    if (ID.TimerQueued)
                        return true;
                    ID.TimerQueued = true;
                }

                if (FunctionName == "control")
                {
                    int held = ((LSL_Types.LSLInteger)param[1]).value;
                    // int changed = ((LSL_Types.LSLInteger)data.Params[2]).value;

                    // If the last message was a 0 (nothing held)
                    // and this one is also nothing held, drop it
                    //
                    if (ID.LastControlLevel == held && held == 0)
                        return true;

                    // If there is one or more queued, then queue
                    // only changed ones, else queue unconditionally
                    //
                    if (ID.ControlEventsInQueue > 0)
                    {
                        if (ID.LastControlLevel == held)
                            return true;
                    }

                    ID.LastControlLevel = held;
                    ID.ControlEventsInQueue++;
                }

                if (FunctionName == "collision")
                {
                    if (ID.CollisionInQueue)
                        return true;
                    if (qParams == null)
                        return true;

                    ID.CollisionInQueue = true;
                }
                // Create a structure and add data
                QueueItemStruct QIS = new QueueItemStruct();
                QIS.ID = ID;
                QIS.functionName = FunctionName;
                QIS.llDetectParams = qParams;
                QIS.param = param;
                if (ID == null)
                    return false;
                QIS.LineMap = ID.LineMap;
                if (World.PipeEventsForScript(
                    QIS.ID.localID))
                {
                    // Add it to queue
                    EventQueue.Enqueue(QIS);
                }
            }
            return true;
        }

        /// <summary>
        /// Prepares to remove the script from the event queue.
        /// </summary>
        /// <param name="itemID"></param>
        public void RemoveFromEventQueue(UUID itemID, uint localID)
        {
            if (!NeedsRemoved.ContainsKey(itemID))
                NeedsRemoved.Add(itemID, localID);
        }

        public Dictionary<UUID, string[]> Errors = new Dictionary<UUID, string[]>();
        /// <summary>
        /// Gets compile errors for the given itemID.
        /// </summary>
        /// <param name="ItemID"></param>
        /// <returns></returns>
        public string[] GetErrors(UUID ItemID)
        {
            while (!Errors.ContainsKey(ItemID))
            {
                Thread.Sleep(250);
            }
            lock (Errors)
            {
                string[] Error = Errors[ItemID];
                Errors.Remove(ItemID);
                if (Error[0] == "SUCCESSFULL")
                    return new string[0];
                return Error;
            }
        }

        private int LoadUnloadMaxQueueSize;
        /// <summary>
        /// Fetches, loads and hooks up a script to an objects events
        /// </summary>
        /// <param name="itemID"></param>
        /// <param name="localID"></param>
        public void StartScript(uint localID, UUID itemID, string Script, int startParam, bool postOnRez, StateSource statesource)
        {
            ScriptData id = null;
            id = GetScript(localID, itemID);
            lock (LUQueue)
            {
                if ((LUQueue.Count >= LoadUnloadMaxQueueSize))
                {
                    m_log.Error("[" + ScriptEngineName + "]: ERROR: Load/unload queue item count is at " + LUQueue.Count + ". Config variable \"LoadUnloadMaxQueueSize\" " + "is set to " + LoadUnloadMaxQueueSize + ", so ignoring new script.");
                    return;
                }
                LUStruct ls = new LUStruct();
                //Its a change of the script source, needs to be recompiled and such.
                if (id != null)
                    ls.Action = LUType.Reupload;
                else
                    ls.Action = LUType.Load;
                if (id == null)
                    id = new ScriptData(this);
                id.World = m_Scene;
                id.localID = localID;
                id.ItemID = itemID;
                id.PostOnRez = postOnRez;
                id.StartParam = startParam;
                id.stateSource = statesource;
                id.State = "default";
                id.Running = true;
                id.Disabled = false;
                id.Source = Script;
                ScriptProtection.RemovePreviouslyCompiled(id.Source);
                ls.ID = id;
                LUQueue.Enqueue(ls);
            }
        }

        public void UpdateScript(uint localID, UUID itemID, string script, int startParam, bool postOnRez, int stateSource)
        {
            ScriptData id = null;
            id = GetScript(localID, itemID);
            //Its a change of the script source, needs to be recompiled and such.
            if (id != null)
            {
                lock (LUQueue)
                {
                    id.PostOnRez = postOnRez;
                    id.StartParam = startParam;
                    id.stateSource = (StateSource)stateSource;
                    id.State = "default";
                    id.Running = true;
                    id.Disabled = false;
                    id.Source = script;
                    id.World = m_Scene;
                    try
                    {
                        id.Start(true);
                    }
                    catch (Exception) { }
                }
            }
            else
            {
                lock (LUQueue)
                {
                    if ((LUQueue.Count >= LoadUnloadMaxQueueSize))
                    {
                        m_log.Error("[" + ScriptEngineName + "]: ERROR: Load/unload queue item count is at " + LUQueue.Count + ". Config variable \"LoadUnloadMaxQueueSize\" " + "is set to " + LoadUnloadMaxQueueSize + ", so ignoring new script.");
                        return;
                    }
                    id = new ScriptData(this);
                    id.ItemID = itemID;
                    id.localID = localID;
                    id.StartParam = startParam;
                    id.stateSource = (StateSource)stateSource;
                    id.State = "default";
                    id.Running = true;
                    id.Disabled = false;
                    id.Source = script;
                    id.PostOnRez = postOnRez;
                    id.World = m_Scene;
                    try
                    {
                        id.Start(true);
                    }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// Disables and unloads a script
        /// </summary>
        /// <param name="localID"></param>
        /// <param name="itemID"></param>
        public void StopScript(uint localID, UUID itemID)
        {
            ScriptData data = GetScript(localID, itemID);
            if (data == null)
                return;
            LUStruct ls = new LUStruct();
            ScriptProtection.RemovePreviouslyCompiled(data.Source);      
            ls.ID = data;
            ls.Action = LUType.Unload;
            RemoveFromEventQueue(itemID, localID);
            lock (LUQueue)
            {
                LUQueue.Enqueue(ls);
            }
        }

        /// <summary>
        /// Gets all itemID's of scripts in the given localID.
        /// </summary>
        /// <param name="localID"></param>
        /// <returns></returns>
        public List<UUID> GetScriptKeys(uint localID)
        {
            List<UUID> UUIDs = new List<UUID>();
            foreach (ScriptData ID in ScriptProtection.GetScript(localID) as ScriptData[])
                UUIDs.Add(ID.ItemID);
            return UUIDs;
        }

        /// <summary>
        /// Gets the script by itemID.
        /// </summary>
        /// <param name="itemID"></param>
        /// <returns></returns>
        public ScriptData GetScriptByItemID(UUID itemID)
        {
            return (ScriptData)ScriptProtection.GetScript(itemID);
        }

        /// <summary>
        /// Gets the InstanceData by the prims local and itemID.
        /// </summary>
        /// <param name="localID"></param>
        /// <param name="itemID"></param>
        /// <returns></returns>
        public ScriptData GetScript(uint localID, UUID itemID)
        {
            return (ScriptData)ScriptProtection.GetScript(localID, itemID);
        }

        /// <summary>
        /// Updates or adds the given InstanceData to the list of known scripts.
        /// </summary>
        /// <param name="id"></param>
        public void UpdateScriptInstanceData(ScriptData id)
        {
            ScriptProtection.AddNewScript(id);
        }

        /// <summary>
        /// Removes the given InstanceData from all known scripts.
        /// </summary>
        /// <param name="id"></param>
        public void RemoveScript(ScriptData id)
        {
            ScriptProtection.RemovePreviouslyCompiled(id.Source);
            ScriptProtection.RemoveScript(id);
        }
    }
    /// <summary>
    /// Queue item structure
    /// </summary>
    public class QueueItemStruct
    {
        public ScriptData ID;
        public string functionName;
        public DetectParams[] llDetectParams;
        public object[] param;
        public Dictionary<KeyValuePair<int, int>, KeyValuePair<int, int>>
                LineMap;
        public int CurrentlyAt = 0;
    }
    public class StateQueueItem
    {
        public ScriptData ID;
        public bool Create;
    }
    // Load/Unload structure
    public class LUStruct
    {
        public ScriptData ID;
        public LUType Action;
    }

    public enum LUType
    {
        Unknown = 0,
        Load = 1,
        Unload = 2,
        Reupload = 3
    }
}
