using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Text.RegularExpressions;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Communications.Cache;

namespace OpenSimEmail.Modules.OpenEmail
{
	public class OpenEmailModule : IEmailModule
	{
		//
		// Log module
		//
		private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		//
		// Module vars
		//
        private IConfigSource m_Config;
		private string m_EmailServer = "";
		private string m_HostName = string.Empty;

        private bool m_Enabled = false;

        // Scenes by Region Handle
        private Dictionary<ulong, Scene> m_Scenes =
            new Dictionary<ulong, Scene>();

        // Queue settings
        private int m_MaxQueueSize = 50; // maximum size of an object mail queue
        private Dictionary<UUID, List<Email>> m_MailQueues = new Dictionary<UUID, List<Email>>();
        private Dictionary<UUID, DateTime> m_LastGetEmailCall = new Dictionary<UUID, DateTime>();
        private TimeSpan m_QueueTimeout = new TimeSpan(2, 0, 0); // 2 hours without llGetNextEmail drops the queue

        public void InsertEmail(UUID to, Email email)
        {
            // It's tempting to create the queue here.  Don't; objects which have
            // not yet called GetNextEmail should have no queue, and emails to them
            // should be silently dropped.

            lock (m_MailQueues)
            {
                if (m_MailQueues.ContainsKey(to))
                {
                    if (m_MailQueues[to].Count >= m_MaxQueueSize)
                    {
                        // fail silently
                        return;
                    }

                    lock (m_MailQueues[to])
                    {
                        m_MailQueues[to].Add(email);
                    }
                }
            }
        }
        
        public void Initialise(Scene scene, IConfigSource m_Config)
		{
            IConfig startupConfig = m_Config.Configs["Startup"];

            m_Enabled = (startupConfig.GetString("emailmodule", "OpenEmailModule") == "OpenEmailModule");

            if (!m_Enabled)
            {
                m_log.Error("[OPENEMAIL] Module is not loaded in OpenSim.ini");
                return;
            }

			IConfig emailConfig = m_Config.Configs["Email"];

                //Load SMTP MODULE config
                try
                {
                    if (emailConfig == null)
                    {
                        m_log.Info("[OPENEMAIL] Not configured, disabling");
                        m_Enabled = false;
                        return;
                    }
                    m_HostName = emailConfig.GetString("host_domain_header_from", "");

                    m_EmailServer = emailConfig.GetString("EmailURL", "");

                    if (m_EmailServer == "")
                    {
                        m_log.Error("[OPENEMAIL] No email dispatcher, disabling email");
                        m_Enabled = false;
                        return;
                    }
                    else
                    {
                        m_log.Info("[OPENEMAIL] OpenEmail module is activated");
                        m_Enabled = true;
                    }

                }
                catch (Exception e)
                {
                    m_log.Error("[EMAIL] OpenEmail module not configured: " + e.Message);
                    m_Enabled = false;
                    return;
                }                

                // It's a go!
                if (m_Enabled)
                {
                    lock (m_Scenes)
                    {
                        // Claim the interface slot
                        scene.RegisterModuleInterface<IEmailModule>(this);

                        // Add to scene list
                        if (m_Scenes.ContainsKey(scene.RegionInfo.RegionHandle))
                        {
                            m_Scenes[scene.RegionInfo.RegionHandle] = scene;
                        }
                        else
                        {
                            m_Scenes.Add(scene.RegionInfo.RegionHandle, scene);
                        }
                    }

                    m_log.Info("[OPENEMAIL] Activated OpenEmail module");
                }
		}

		public void PostInitialise()
		{
			if (!m_Enabled)
				return;
		}

		public void Close()
		{
		}

		public string Name
		{
            get { return "OpenEmailModule"; }
		}

		public bool IsSharedModule
		{
			get { return true; }
		}

		/// New Client Event Handler
        private void OnNewClient(IClientAPI client)
        {
            return;
        }

        // Functions needed inside

        private void DelayInSeconds(int delay)
        {
            delay = (int)((float)delay * 1000);
            if (delay == 0)
                return;
            System.Threading.Thread.Sleep(delay);
        }

        static DateTime ConvertFromUnixTimestamp(double timestamp)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return origin.AddSeconds(timestamp);
        }


        static double ConvertToUnixTimestamp(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            TimeSpan diff = date - origin;
            return Math.Floor(diff.TotalSeconds);
        }

        private SceneObjectPart findPrim(UUID objectID, out string ObjectRegionName)
        {
            lock (m_Scenes)
            {
                foreach (Scene s in m_Scenes.Values)
                {
                    SceneObjectPart part = s.GetSceneObjectPart(objectID);
                    if (part != null)
                    {
                        ObjectRegionName = s.RegionInfo.RegionName;
                        uint localX = (s.RegionInfo.RegionLocX * 256);
                        uint localY = (s.RegionInfo.RegionLocY * 256);
                        ObjectRegionName = ObjectRegionName + " (" + localX + ", " + localY + ")";
                        return part;
                    }
                }
            }
            ObjectRegionName = string.Empty;
            return null;
        }

        private void resolveNamePositionRegionName(UUID objectID, out string ObjectName, out string ObjectAbsolutePosition, out string ObjectRegionName)
        {
            string m_ObjectRegionName;
            int objectLocX;
            int objectLocY;
            int objectLocZ;
            SceneObjectPart part = findPrim(objectID, out m_ObjectRegionName);
            if (part != null)
            {
                objectLocX = (int)part.AbsolutePosition.X;
                objectLocY = (int)part.AbsolutePosition.Y;
                objectLocZ = (int)part.AbsolutePosition.Z;
                ObjectAbsolutePosition = "(" + objectLocX + ", " + objectLocY + ", " + objectLocZ + ")";
                ObjectName = part.Name;
                ObjectRegionName = m_ObjectRegionName;
                return;
            }
            objectLocX = (int)part.AbsolutePosition.X;
            objectLocY = (int)part.AbsolutePosition.Y;
            objectLocZ = (int)part.AbsolutePosition.Z;
            ObjectAbsolutePosition = "(" + objectLocX + ", " + objectLocY + ", " + objectLocZ + ")";
            ObjectName = part.Name;
            ObjectRegionName = m_ObjectRegionName;
            return;
        }

        //
		// Make external XMLRPC request
		//
		private Hashtable GenericXMLRPCRequest(Hashtable ReqParams, string method)
		{
			ArrayList SendParams = new ArrayList();
			SendParams.Add(ReqParams);

			// Send Request
			XmlRpcResponse Resp;
			try
			{
				XmlRpcRequest Req = new XmlRpcRequest(method, SendParams);
                Resp = Req.Send(m_EmailServer, 30000);
			}
			catch (WebException ex)
			{
                m_log.ErrorFormat("[OPENEMAIL]: Unable to connect to Email " +
                        "Server {0}.  Exception {1}", m_EmailServer, ex);

				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to send email at this time. ";
				ErrorHash["errorURI"] = "";

				return ErrorHash;
			}
			catch (SocketException ex)
			{
				m_log.ErrorFormat(
                        "[OPENEMAIL]: Unable to connect to Email Server {0}. " +
                        "Exception {1}", m_EmailServer, ex);

				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to send email at this time. ";
				ErrorHash["errorURI"] = "";

				return ErrorHash;
			}
			catch (XmlException ex)
			{
				m_log.ErrorFormat(
                        "[OPENEMAIL]: Unable to connect to Email Server {0}. " +
                        "Exception {1}", m_EmailServer, ex);

				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to send email at this time. ";
				ErrorHash["errorURI"] = "";

				return ErrorHash;
			}
			if (Resp.IsFault)
			{
				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to send email at this time. ";
				ErrorHash["errorURI"] = "";
				return ErrorHash;
			}
			Hashtable RespData = (Hashtable)Resp.Value;

			return RespData;
		}

        public void SendEmail(UUID objectID, string address, string subject, string body)
        {
            //Check if address is empty
            if (address == string.Empty)
                return;

            //FIXED:Check the email is correct form in REGEX
            string EMailpatternStrict = @"^(([^<>()[\]\\.,;:\s@\""]+"
                + @"(\.[^<>()[\]\\.,;:\s@\""]+)*)|(\"".+\""))@"
                + @"((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}"
                + @"\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+"
                + @"[a-zA-Z]{2,}))$";
            Regex EMailreStrict = new Regex(EMailpatternStrict);
            bool isEMailStrictMatch = EMailreStrict.IsMatch(address);
            if (!isEMailStrictMatch)
            {
                m_log.Error("[OPENEMAIL] REGEX Problem in EMail Address: " + address);
                return;
            }
            //FIXME:Check if subject + body = 4096 Byte
            if ((subject.Length + body.Length) > 1024)
            {
                m_log.Error("[OPENEMAIL] subject + body > 1024 Byte");
                return;
            }

            string LastObjectName = string.Empty;
            string LastObjectPosition = string.Empty;
            string LastObjectRegionName = string.Empty;

            resolveNamePositionRegionName(objectID, out LastObjectName, out LastObjectPosition, out LastObjectRegionName);

            Hashtable ReqHash = new Hashtable();
            ReqHash["fromaddress"] = objectID.ToString() + "@" + m_HostName;
            ReqHash["toaddress"] = address.ToString();
            ReqHash["timestamp"] = ConvertToUnixTimestamp(DateTime.UtcNow);
            ReqHash["subject"] = subject.ToString();
            ReqHash["objectname"] = LastObjectName;
            ReqHash["position"] = LastObjectPosition;
            ReqHash["region"] = LastObjectRegionName;
            ReqHash["messagebody"] = body.ToString();

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "send_email");

            if (!Convert.ToBoolean(result["success"]))
            {
                return;
            }
            DelayInSeconds(20);
        }

        public Email GetNextEmail(UUID objectID, string sender, string subject)
        {

            string m_Object;
            string num_emails = "";

            m_Object = objectID + "@" + m_HostName;

            Hashtable ReqHash = new Hashtable();
            ReqHash["objectid"] = m_Object;

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "check_email");

            if (!Convert.ToBoolean(result["success"]))
            {
                return null;
            }
            
            ArrayList dataArray = (ArrayList)result["data"];

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                num_emails = d["num_emails"].ToString();
            }

            //m_log.Error("[OPENEMAIL] " + num_emails);

            DelayInSeconds(2);

            if(num_emails != "0")
            {
                // Get the info from the database and Queue it up
                Hashtable GetHash = new Hashtable();
                GetHash["objectid"] = m_Object;
                GetHash["number"] = num_emails;

                //m_log.Debug("[OPENEMAIL] I have " + num_emails + " waiting on dataserver");

                Hashtable results = GenericXMLRPCRequest(GetHash,
                        "retrieve_email");

                if (!Convert.ToBoolean(results["success"]))
                {
                    return null;
                }

                ArrayList mailArray = (ArrayList)results["data"];

                foreach (Object ob in mailArray)
                {
                    Hashtable d = (Hashtable)ob;

                    Email email = new Email();
                    // Debugging
                    
                    //m_log.Debug("[OPENEMAIL] Time: " + d["timestamp"].ToString());
                    //m_log.Debug("[OPENEMAIL] Subject: " + d["subject"].ToString());
                    //m_log.Debug("[OPENEMAIL] Sender: " + d["sender"].ToString());
                    //m_log.Debug("[OPENEMAIL] Object: " + d["objectname"].ToString());
                    //m_log.Debug("[OPENEMAIL] Region: " + d["region"].ToString());
                    //m_log.Debug("[OPENEMAIL] Local-Position: " + d["objectpos"].ToString());
                    //m_log.Debug("[OPENEMAIL] Message: " + d["message"].ToString());
                    
                    email.time = d["timestamp"].ToString();
                    email.subject = d["subject"].ToString();
                    email.sender = d["sender"].ToString();
                    email.message = "Object-Name: " + d["objectname"].ToString() +
                                  "\nRegion: " + d["region"].ToString() + "\nLocal-Position: " +
                                  d["objectpos"].ToString() + "\n\n" + d["message"].ToString();

                    string guid = m_Object.Substring(0, m_Object.IndexOf("@"));
                    UUID toID = new UUID(guid);

                    InsertEmail(toID, email);
                }
            }

            // And let's start with readin the Queue here
            List<Email> queue = null;

            lock (m_LastGetEmailCall)
            {
                if (m_LastGetEmailCall.ContainsKey(objectID))
                {
                    m_LastGetEmailCall.Remove(objectID);
                }

                m_LastGetEmailCall.Add(objectID, DateTime.Now);

                // Hopefully this isn't too time consuming.  If it is, we can always push it into a worker thread.
                DateTime now = DateTime.Now;
                List<UUID> removal = new List<UUID>();
                foreach (UUID uuid in m_LastGetEmailCall.Keys)
                {
                    if ((now - m_LastGetEmailCall[uuid]) > m_QueueTimeout)
                    {
                        removal.Add(uuid);
                    }
                }

                foreach (UUID remove in removal)
                {
                    m_LastGetEmailCall.Remove(remove);
                    lock (m_MailQueues)
                    {
                        m_MailQueues.Remove(remove);
                    }
                }
            }

            lock (m_MailQueues)
            {
                if (m_MailQueues.ContainsKey(objectID))
                {
                    queue = m_MailQueues[objectID];
                }
            }

            if (queue != null)
            {
                lock (queue)
                {
                    if (queue.Count > 0)
                    {
                        int i;

                        for (i = 0; i < queue.Count; i++)
                        {
                            if ((sender == null || sender.Equals("") || sender.Equals(queue[i].sender)) &&
                                (subject == null || subject.Equals("") || subject.Equals(queue[i].subject)))
                            {
                                break;
                            }
                        }

                        if (i != queue.Count)
                        {
                            Email ret = queue[i];
                            queue.Remove(ret);
                            ret.numLeft = queue.Count;
                            return ret;
                        }
                    }
                }
            }
            else
            {
                lock (m_MailQueues)
                {
                    m_MailQueues.Add(objectID, new List<Email>());
                }
            }

            return null;
        }
	}
}
