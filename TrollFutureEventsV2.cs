#region Eliza.Metras.TrollFutureEventsV2
#endregion
using Eliza.Common.AmazonWebServices.AwsDataAccess;
using Eliza.MetrasBase.MetrasBaseLib.Service;
using Eliza.MetrasBase.MetrasBaseLib.Service.Base;
using Eliza.MetrasBase.MetrasBaseLib.Service.Config;
using Eliza.MetrasBase.MetrasBaseLib.Shared;
using Eliza.MetrasBase.MetrasCodeRunner.CodeRunner.GenericCodeRunner;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eliza.CCoord.CustomersProgramsAndApplications.CpaClientLib;
using Eliza.CCoord.CustomersProgramsAndApplications.CpaObjectsLib;
using Eliza.CCoord.CloudCoordinator.CloudCoordinatorObjectsLib;
using Eliza.CCoord.CloudCoordinator.CloudCoordinatorClientLib;
using Eliza.Common.TplHelpers;

namespace Eliza.Metras
{
    /// <summary>
    /// Fetch entries from the FutureEvents table that are ready to be executed
    /// and place them in an SQS Queue for other instances to process.
    /// </summary>
    public class TrollFutureEventsV2 : MetrasBaseScheduledWorkRequest
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TrollFutureEventsV2"/> class.
		/// </summary>
		public TrollFutureEventsV2() : base("TrollFutureEventsV2", "TrollFutureEventsV2", "") { }
		/// <summary>
		/// Initializes a new instance of the <see cref="TrollFutureEventsV2"/> class.
		/// </summary>
		/// <param name="sourceCodeNameSpace">The source code name space.</param>
		public TrollFutureEventsV2(string sourceCodeNameSpace) : base("TrollFutureEventsV2", "TrollFutureEventsV2", sourceCodeNameSpace) { }
		/// <summary>
		/// Runs the rule.
		/// </summary>
		/// <param name="executionArguments">The execution arguments.</param>
		/// <param name="parameterDetailList">The parameter detail list.</param>
		public override void RunCode(object[] executionArguments, ParameterDetailList parameterDetailList)
		{
			#region Set the Execution Arguments
			if (!this.SelfTest)
				setupExecutionAgruments(executionArguments);
			else
				setupSelfTestExecutionAgruments(out executionArguments, parameterDetailList);
            #endregion Set the Execution Arguments

            int httpTimeout = httpTimeoutSeconds;

            if (stateDetail.NameValues.ContainsKey("MetrasWebServices"))
			{
				string ReturnXml = ProcessRequest();

				Av.UpdateAttributeValue(stateDetail.NameValues, "ReturnXml", ReturnXml);
				Av.UpdateAttributeValue(stateDetail.NameValues, "ContentType", "");

				return;
			}
			else
			{
				try
				{
                    // if this is a direct call from another fragment in the same process,
                    // call back to self thru TPL Lib

                    Func<string> callback = () =>
                    {
                        return ProcessRequest();
                    };
                    string responseProcessRequest = TimedTaskRunner.Run<string>(callback, TimeSpan.FromSeconds(httpTimeout));
                    if (responseProcessRequest != "Okay")
                    {
                        Logging.Log("EXCEPTION: ProcessRequest " + this.Name + " " + responseProcessRequest, 1);
                        if (!ReadOnly) // this is for Testing
                            SNSRequests.Publish(this.Name, "EXCEPTION", "Exception " + this.Name, "EXCEPTION: ProcessRequest " + this.Name + " " + responseProcessRequest, true);
                    }
                }
				catch (Exception e)
				{
					Logging.Log(string.Format("EXCEPTION TPL: {0} Error:{1}", this.Name, e.ToString()), 1);
					if (!ReadOnly) // this is for Testing
						SNSRequests.Publish(this.Name, "EXCEPTION", "Exception " + this.Name, string.Format("EXCEPTION TPL: {0} Error:{1}", this.Name, e.Message), true);
				}
			}
			return;
		}

		/// <summary>
		/// Processes the future events.
		/// </summary>
		public string ProcessRequest()
		{
			int queryLimit = 500;
            string messengerTopicName = "TrollFutureEventsV2";
            //string futureEventSqsName = "QueuedFutureEvents";
            long futureEventsProcessed = 0;
            const int maxThreadsPerFutureEventEntries = 20;

            string futureEventsUrl = "http://localhost/AWS01/FutureEvents.asmx/";
            // Could also use something like this which would require a change
            // to MetrasBaseLib.  ElizaShared.Default.httpDataRecordSaveUrl;

            // Used to determine if the request has worked longer than the httpTimeoutSeconds.
            // If the request goes longer than the http request, it will cause a hung thread.
            // PLATFORM-1139
            DateTime requestStartTimestamp = DateTime.UtcNow;
            bool stopWork = false;

            string msg = string.Format("[{0}] {1}.ProcessRequest(): Starting, queryLimit: {2}", GetFile.GetHostInfo(), this.Name, queryLimit);
            Logging.Log(msg, 1);
            MessengerRequests.SendMessage("information", msg, messengerTopicName, "", true);


            DateTime startTime = DateTime.Now;
			IDictionary<string, long> FutureEventsList = new Dictionary<string, long>();

            try
            {
                #region - Create a heartbeat file

                try
                {
                    if (!ReadOnly) // this is for Testing
                    {
                        Boolean uploadStatus = FileUtils.UpdateHeartbeatFile(OperatingEnvironment.Current, heartbeatFilename);

                        if (!uploadStatus)
                        {
                            string sensorMsg = string.Format("ERROR:UploadHeartbeatFile:Failed:File={0}:TrollFutureEventsV2.ProcessRequest", heartbeatFilename);
                            Logging.Log(string.Format("[{0}] {1}", GetFile.GetHostInfo(), sensorMsg), 1);
                            Logging.TrySendToEngineStatsDatabase(this.Name, sensorMsg, 0, TimeSpan.FromSeconds(0));
                        }
                    }
                }
                catch (Exception e)
                {
                    Logging.Log(string.Format("EXCEPTION: {0}.ProcessRequest() FileUtils.UpdateHeartbeatFile FAILED '{1}' ", this.Name, e), 1);
                }

                #endregion - Create a heartbeat file

                // Make sure the CloudCoordinator cache is refreshed periodically.  Yea Bill!!!!
                bool refreshed = UntypedFactory.RefreshAllInstances(TimeSpan.FromMinutes(10));
                if (refreshed)
                    Logging.Log(string.Format("{0}.ProcessRequest() RefreshAllInstances executed.", Name), 1);

                #region - Get a list of active customers to use to create a list of FutureEvents tables to process

                List<string> activeCustomerList = new List<string>();
                try
                {
                    activeCustomerList = GetActiveCustomerList();
                }
                catch (Exception fetchActiveCustomerListException)
                {
                    Logging.Log(string.Format("{0}.ProcessRequest(): EXCEPTION: GetActiveCustomerList(): FAILED '{1}' ", this.Name, fetchActiveCustomerListException), 1);
                }
                
                // Read DynamDB for List of Future Events Table
                //   Read each Future Events Table for count and return name and count
                //   The data is stored in Aurora now but this method still returns
                //   the same list that Dynamo did.
                IList<string> tableList = AwsDispatcherDynamoAuroraMigration.DynamoCache(OperatingEnvironment.Current).GetTableList().Where(x => x.Contains(".FutureEvents")).ToList();
                
                foreach (string table in tableList)
                {
                    // If the ActiveCustomerList is not populated, process all tables
                    if (activeCustomerList.Count <= 0)
                        FutureEventsList.Add(table, 0);
                    else
                    {   
                        // Only process tables for active customers
                        if (activeCustomerList.Contains(table.Split('.').FirstOrDefault()))
                            FutureEventsList.Add(table, 0);
                    }
                }

                msg = string.Format("[{0}] {1}.ProcessRequest(): Found {2} .FutureEvent tables for {3} Active Customers.", GetFile.GetHostInfo(), this.Name, FutureEventsList.Count, activeCustomerList.Count);
                Logging.Log(msg, 1);
                MessengerRequests.SendMessage("information", msg, messengerTopicName, "", true);

                #endregion - Get a list of active customers to use to create a list of FutureEvents tables to process

                // Create the MetrasEvenrPrioritiesFactory to be used below as well as the dictionary of factories.
                Eliza.Metras.MetrasEventPrioritiesFactory metrasEventPrioritiesFactory = new Eliza.Metras.MetrasEventPrioritiesFactory();

                #region - Foreach EventType, fetch and process data from each table

                foreach (EventTypes Events in Enum.GetValues(typeof(EventTypes)))
                {
                    if (Events != EventTypes.NovaAppDelay)
                    {

                        if (!Status.ExecuteClassListEventType(Events))
                            continue;

                        if (Events == EventTypes.OutReachResult || Events == EventTypes.OutReachImport)
                            continue;
                    }

                    if (stopWork)
                        break;

                    // Process each FutureEvents table and process some data
                    foreach (KeyValuePair<string, long> FutureEvents in FutureEventsList)
                    //Parallel.ForEach(FutureEventsList.Skip(ProcessCount).Take(Take).ToList(), FutureEvents =>
                    {
                        if (stopWork)
                            break;

                        // Find all table entries that should be executed now
                        IDictionary<string, IList<string>> RangeKeyCondition = new Dictionary<string, IList<string>>();
                        DateTime dtNow = DateTime.UtcNow;
                        // For SQA only
                        if (this.ExecuteClass.OptionalParameters.Contains("Date"))
                            dtNow = Convert.ToDateTime(this.ExecuteClass.OptionalParameters["Date"].Value);

                        RangeKeyCondition.Add("LE", new List<string>() { string.Format("{0:yyyy-MM-dd HH:mm:ss.fff}", dtNow) });
                        string queryJobId = null;

                        IList<IDictionary<string, string>> fes = null;
                        try
                        {
                            fes = AwsFutureEventsDynamoAuroraMigration.DynamoCache(OperatingEnvironment.Current).QueryEntities(FutureEvents.Key, queryJobId, Events.ToString(), RangeKeyCondition, queryLimit);
                        }
                        catch (Exception e)
                        {
                            if (e.Message.Contains("The level of configured provisioned throughput for the table was exceeded"))
                                LoopUpTable.SetTableProvision(FutureEvents.Key, true, false);
                            else
                            {
                                string exception = string.Format("{0}.ProcessRequest(): EXCEPTION: {1} FutureEvents FAILED Exception: {2}",
                                    this.Name,
                                    OperatingEnvironment.Current,
                                    e);

                                Logging.Log(exception, 1);
                                if (!ReadOnly) // this is for Testing
                                    SNSRequests.Publish(this.Name, "EXCEPTION", "Exception " + this.Name, exception, true);
                            }
                            //return;
                            continue;
                        }

                        if (fes.Count > 0)
                        {
                            string msg4 = string.Format("[{0}] {1}.ProcessRequest(): Fetched FutureEvents Event: {2}, Table: {3}, Count: {4}", GetFile.GetHostInfo(), this.Name, Events.ToString(), FutureEvents.Key, fes.Count);
                            Logging.Log(msg4, 1);
                            MessengerRequests.SendMessage("information", msg4, messengerTopicName, "", true);
                        }
                        else
                            //return;
                            continue;

                        #region - Now process each item and place it into an SQS Queue

                        long futureEventsQueued = 0;

                        //foreach (IDictionary<string, string> fe in fes)
                        Parallel.ForEach(fes, new ParallelOptions { MaxDegreeOfParallelism = maxThreadsPerFutureEventEntries }, fe =>
                        {
                            if (stopWork)
                                //break;
                                return;

                            DateTime startProcessingDateTime = DateTime.UtcNow;

                            string debugKey = string.Empty;

                            try
                            {
                                // Capture some information
                                string jobId = string.Empty;
                                if (fe.ContainsKey("JobId"))
                                    jobId = fe["JobId"];
                                debugKey = string.Format("{0}:{1}:{2}:{3}:{4}:{5}:{6}", Av.Get(fe, StandardFactPaths.CustomerName), Av.Get(fe, StandardFactPaths.ProgramName), Av.Get(fe, "EventType"), Av.Get(fe, StandardFactPaths.ElizaEntityId), jobId, Av.Get(fe, "EvaluateTime"), Av.Get(fe, "RangeKey"));


                                // Create a StateDetail for the work
                                StateDetail entryStateDetail = new StateDetail();
                                entryStateDetail.EventType = Events;
                                entryStateDetail.Url = futureEventsUrl;
                                //entryStateDetail.WorkType = "Events";

                                entryStateDetail.NameValues.Add("FutureEventsTableName", FutureEvents.Key);
                                Av.UpdateStateDetailDataStructures(entryStateDetail, "FutureEvent", fe);

                                // Determine the SQS Queue name to place the item in
                                // Get the current event names and their priorities by ProgramName
                                // Tries at the Program level, then Customer level, then Environment level and then Global level
                                // Global contains a default which can be overridden at lower levels
                                IDictionary <EventTypes, MetrasEventPriority> metrasEventPriorities = metrasEventPrioritiesFactory.Get(OperatingEnvironment.Current, Av.Get(fe, StandardFactPaths.ProgramName), UntypedFactory.Factories);

                                string futureEventSqsName = DetermineSqsQueueNameToUseBasedOnEventName(Events, metrasEventPriorities);

                                // Create a QueuedDefinition for the work
                                QueueDefinition entryQd = new QueueDefinition(entryStateDetail.ToXml(), futureEventSqsName, EventTypes.None);

                                // Add to the SQS Queue
                                bool writeToSqsQueueSuccess = false;
                                try
                                {
                                    SQS.SQSCache(OperatingEnvironment.Current).WriteToSQS(futureEventSqsName, entryQd.ToXml());
                                    writeToSqsQueueSuccess = true;
                                }
                                catch (Exception sqsException)
                                {
                                    // Oops, something went wrong.  Add to MaxAttemptRetry to try again
                                    int retryTime = (ScheduledWorkRequestsConfig.RetrySeconds + RandomTime.Next(20)) * Attempts;
                                    entryQd.ExceptionDateTime = DateTime.UtcNow;
                                    entryQd.Exception += sqsException.Message;
                                    RetryHelp.WriteToSystemMaxAttemptRetry(ReadOnly, "TrollFutureEvents", entryQd, true, retryTime);
                                }

                                if (TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains(Av.Get(fe, StandardFactPaths.CustomerName)) || (!OperatingEnvironment.IsCurrentProduction && TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains("all")))
                                {
                                    string eventName = string.Format("{0}:FutureEventEntry{1}ToSQSQueue:QueueName={2}", debugKey, writeToSqsQueueSuccess ? "SuccessfullyAdded" : "FailedAdding", futureEventSqsName);
                                    Logging.TrySendToEngineStatsDatabase(this.Name, eventName, 1, (DateTime.UtcNow - startProcessingDateTime));
                                }

                                #region - Delete the entry from the FutureEvents table

                                string returnValue = DeleteEntry(FutureEvents.Key, Av.Get(fe, "Key"), Av.Get(fe, "RangeKey"), jobId);
                                if (!returnValue.Contains("Error"))
                                {
                                    Logging.Log(string.Format("{0}.ProcessRequest(): DELETE Successful...EntityType: Table: {1} Key: '{2}', RangeKey: '{3}', jobId: '{4}'", this.Name, FutureEvents.Key, Av.Get(fe, "Key"), Av.Get(fe, "RangeKey"), jobId ?? ""), 2);

                                    if (TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains(Av.Get(fe, StandardFactPaths.CustomerName)) || (!OperatingEnvironment.IsCurrentProduction && TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains("all")))
                                    {
                                        string eventName = string.Format("{0}:FutureEventEntrySuccessfullyDeleted", debugKey);
                                        Logging.TrySendToEngineStatsDatabase(this.Name, eventName, 1, (DateTime.UtcNow - startProcessingDateTime));
                                    }
                                }
                                else
                                {
                                    if (returnValue.Contains("The level of configured provisioned throughput for the table was exceeded"))
                                    {
                                        #region - Dynamo Privisioning Error

                                        if (TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains(Av.Get(fe, StandardFactPaths.CustomerName)) || (!OperatingEnvironment.IsCurrentProduction && TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains("all")))
                                        {
                                            string eventName = string.Format("{0}:DeleteFailed:DynamoProvisioningError", debugKey);
                                            Logging.TrySendToEngineStatsDatabase(this.Name, eventName, 1, (DateTime.UtcNow - startProcessingDateTime));
                                        }

                                        StateDetail sd = new StateDetail();
                                        sd.NameValues.Add("DeleteEntity", "");
                                        sd.NameValues.Add("TableName", FutureEvents.Key);
                                        sd.NameValues.Add("Key", Av.Get(fe, "Key"));
                                        sd.NameValues.Add("RangeKey", Av.Get(fe, "RangeKey"));
                                        if (!string.IsNullOrWhiteSpace(jobId))
                                            sd.NameValues.Add("JobId", jobId);

                                        QueueDefinition qd = new QueueDefinition(sd.ToXml(), "QueuedEventFailedSaveFuture", EventTypes.None);
                                        qd.ExceptionDateTime = DateTime.UtcNow;
                                        qd.Exception = returnValue;
                                        try
                                        {
                                            if (!ReadOnly) // this is for Testing
                                                SQS.SQSCache(OperatingEnvironment.Current).WriteToSQS("QueuedEventFailedSaveFuture", qd.ToXml(), 60);
                                        }
                                        catch (Exception ex)
                                        {
                                            qd.ExceptionDateTime = DateTime.UtcNow;
                                            qd.Exception += ex.Message;
                                            RetryHelp.WriteToSystemMaxAttemptRetry(ReadOnly, this.Name, qd);
                                        }
                                        LoopUpTable.SetTableProvision(FutureEvents.Key, false, true);

                                        #endregion - Dynamo Privisioning Error
                                    }
                                    else
                                    {
                                        Logging.Log(string.Format("{0}.ProcessRequest(): Delete FAILED Table: {1} Key: '{2}', RangeKey: '{3}', jobId: '{4}', returnValue='{5}'", this.Name, FutureEvents.Key, Av.Get(fe, "Key"), Av.Get(fe, "RangeKey"), jobId ?? "", returnValue), 1);
                                        //if (TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains(Av.Get(fe, StandardFactPaths.CustomerName)) || (!OperatingEnvironment.IsCurrentProduction && TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains("all")))
                                        //{
                                        string eventName = string.Format("ERROR:DeleteFailed:{0}:returnValue={1}", debugKey, returnValue);
                                        if (eventName.Length > 450)
                                            eventName = new string(eventName.Take(450).ToArray());
                                        Logging.TrySendToEngineStatsDatabase(this.Name, eventName, 1, (DateTime.UtcNow - startProcessingDateTime));
                                        //}
                                    }
                                }

                                #endregion - Delete the entry from the FutureEvents table

                                Interlocked.Increment(ref futureEventsProcessed);
                                Interlocked.Increment(ref futureEventsQueued);
                            }
                            catch (Exception processEntryException)
                            {
                                string jobId = string.Empty;
                                if (fe.ContainsKey(StandardFactPaths.JobId))
                                    jobId = fe[StandardFactPaths.JobId];

                                string exception = string.Format("{0}.ProcessRequest(): EXCEPTION: {1} Table: {2} Key: '{3}', RangeKey: '{4}', jobId: '{5}', {6}",
                                    this.Name,
                                    OperatingEnvironment.Current,
                                    FutureEvents.Key, Av.Get(fe, "Key"), Av.Get(fe, "RangeKey"), jobId ?? "", processEntryException);

                                Logging.Log(exception, 1);
                                if (!ReadOnly) // this is for Testing
                                    SNSRequests.Publish(this.Name, "EXCEPTION", "Exception " + this.Name, exception, true);

                                if (TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains(Av.Get(fe, StandardFactPaths.CustomerName)) || (!OperatingEnvironment.IsCurrentProduction && TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging.Contains("all")))
                                {
                                    string eventName = string.Format("{0}:ExceptionDuringProcessing:{1}", debugKey, processEntryException);
                                    if (eventName.Length > 450)
                                        eventName = new string(eventName.Take(450).ToArray());
                                    Logging.TrySendToEngineStatsDatabase(this.Name, eventName, 1, TimeSpan.FromSeconds(0));
                                }
                            }

                        });

                        Logging.Log(string.Format("[{0}] {1}.ProcessRequest(): Completed Queuing FutureEvents from {2} Count: {3}", GetFile.GetHostInfo(), this.Name, FutureEvents.Key, futureEventsQueued), 1);

                        if ((DateTime.UtcNow - requestStartTimestamp).TotalSeconds >= totalWorkTimeSeconds)
                        {
                            stopWork = true;
                            break;
                        }

                        #endregion - Now process each item and place it into an SQS Queue
                    }
                }

                #endregion - Foreach EventType, fetch and process data from each table
            }
            catch (Exception e)
            {
                string exception = string.Format("{0}.ProcessRequest(): EXCEPTION: {1} FutureEvents FAILED Exception: {2}",
                    this.Name,
                    OperatingEnvironment.Current,
                    e);

                Logging.Log(exception, 1);
                if (!ReadOnly) // this is for Testing
                    SNSRequests.Publish(this.Name, "EXCEPTION", "Exception " + this.Name, exception, true);
            }

			try
			{
				DateTime endTime = DateTime.Now;
				TimeSpan processTime = endTime - startTime;

				double d = (futureEventsProcessed / (double)processTime.TotalSeconds);
                string msg3 = string.Format("[{0}] {1}.ProcessRequest(): TIMINGS:{1}:rec:{2}:sec:{3}:r/s:{4}:stopWork:{5}",
                    GetFile.GetHostInfo(),
                    this.Name,
                    futureEventsProcessed.ToString(),
                    processTime.TotalSeconds.ToString(),
                    processTime.TotalSeconds > 0 ? d.ToString("#0.00") : FutureEventsList.Count.ToString(),
                    stopWork.ToString());

                Logging.TrySendToEngineStatsDatabase(this.Name, string.Format("TIMINGS:TrollFutureEventsV2:ProcessRequest:FutureEventsProcessed={0}:stopWork={1}", futureEventsProcessed, stopWork.ToString()), futureEventsProcessed, processTime);

                Logging.Log(msg3, 1);
                MessengerRequests.SendMessage("information", msg3, messengerTopicName, "", true);
            }
			catch { }

            string dts = MiscUtils.CalculateDuration(startTime, DateTime.Now);
            string msg1 = string.Format("[{0}] {1}.ProcessRequest(): Done, FutureEventsProcessed='{2}', Operating took: {3} (H:M:S.FFF), stopWork: {4}", GetFile.GetHostInfo(), this.Name, futureEventsProcessed, dts, stopWork.ToString());
            Logging.Log(msg1, 1);
            MessengerRequests.SendMessage("information", msg1, messengerTopicName, "", true);

            return "Okay";
		}

        #region - Variables

        /// <summary>
        /// http Timeout Seconds
        /// </summary>
        private static int httpTimeoutSeconds = 600;
        /// <summary>
        /// Total Work Time in Seconds
        /// </summary>
        private static int totalWorkTimeSeconds = httpTimeoutSeconds - 30;

        /// <summary>
        /// Used if an error occurs writing to SQS Queue
        /// </summary>
        private static readonly Random RandomTime = new Random();

        /// <summary>
        /// FutureEvent Queue Names with priority
        /// </summary>
        public static string[] FutureEventQueueNames = new string[]
        {
            // Assumes that the first one has highest priority
			"QueuedFutureEvents-PriorityHigh",
            "QueuedFutureEvents-PriorityMedium",
            "QueuedFutureEvents-PriorityLow"
        };

        // Heartbeat filename
        private static string heartbeatFilename = string.Format("HeartbeatFiles/{0}Timestamp_{1}", "TrollFutureEventsV2", GetFile.GetHostInfo());

        /// <summary>
        /// TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefault
        /// </summary>
        private static List<string> TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefault = new List<string>();
        /// <summary>
        /// TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefaultXX
        /// </summary>
        private List<string> TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefaultXX;
        /// <summary>
        /// List of CustomerNames to use for debugging purposes or all for all operations
        /// (only in non production.operations environments.
        /// </summary>
        private List<string> TrollFutureEventsV2ListOfCustomerNamesCaptureDebugging
        {
            get
            {
                TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefaultXX = TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefault;

                try
                {
                    TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefaultXX = MetrasManagerConfigCache.MetrasServiceCachedConfigEntryToList("TrollFutureEvents_ListOfCustomerNamesCaptureDebugging");
                }
                catch { }

                return TrollFutureEventsV2ListOfCustomerNamesCaptureDebuggingDefaultXX;
            }
        }

        #endregion - Variables


        #region - Helpers

        /// <summary>
        /// Determine the SQS Queue name to use based on the EventType.
        /// If mapping is not found, uses the lowest priority queue.
        /// </summary>
        /// <param name="givenEvent">Given EventType</param>
        /// <param name="eventPriorities">Event Priorities (Event, Priority(high, medium, low)</param>
        /// <returns>SQS Queue name to use</returns>
        private string DetermineSqsQueueNameToUseBasedOnEventName(EventTypes givenEvent, IDictionary<EventTypes, MetrasEventPriority> metrasEventPriorities)
        {
            // Select a default name (Lowest priority)
            string sqsQueueName = FutureEventQueueNames.Where(x => x.EndsWith("Low")).First();

            // If no configuration was found, use the above default
            if (metrasEventPriorities != null)
            {
                try
                {
                    if (metrasEventPriorities.ContainsKey(givenEvent))
                    {
                        sqsQueueName = FutureEventQueueNames.Where(x => x.EndsWith(metrasEventPriorities[givenEvent].ToString())).First();
                    }
                    else
                    {
                        string msg = string.Format("Could not determine SQS Queue name for given EventType: {1}, metrasEventPriorities: {2}, using default QueueName: {3}", this.Name, givenEvent.ToString(), JSONSerializer<IDictionary<EventTypes, MetrasEventPriority>>.ToJson(metrasEventPriorities), sqsQueueName);
                        Logging.Log(string.Format("{0}.DetermineSqsQueueNameToUseBasedOnEventName(): WARNING {1}", this.Name, msg), 1);
                        Logging.TrySendToEngineStatsDatabase(this.Name, string.Format("WARNING:DetermineSqsQueueNameToUseBasedOnEventName(): {0}", msg), 0, TimeSpan.FromSeconds(0));
                    }
                }
                catch (Exception ex)
                {
                    string msg = string.Format("Could not determine SQS Queue name for given EventType: {1}, metrasEventPriorities: {2}, using default QueueName: {3}, Error: {4}", this.Name, givenEvent.ToString(), JSONSerializer<IDictionary<EventTypes, MetrasEventPriority>>.ToJson(metrasEventPriorities), sqsQueueName, ex);
                    Logging.Log(string.Format("{0}.DetermineSqsQueueNameToUseBasedOnEventName(): EXCEPTION: {1}", this.Name, msg), 1);

                    string eventName = String.Format("WARNING:DetermineSqsQueueNameToUseBasedOnEventName(): {0}",  msg);
                    if (eventName.Length > 450)
                        eventName = new string(eventName.Take(450).ToArray());
                    Logging.TrySendToEngineStatsDatabase(this.Name, eventName, 0, TimeSpan.FromSeconds(0));
                }
            }
            else
            {
                string msg = string.Format("metrasEventPriorities object was null, using default QueueName: {1}", this.Name, sqsQueueName);
                Logging.Log(string.Format("{0}.DetermineSqsQueueNameToUseBasedOnEventName(): WARNING {1}", this.Name, msg), 1);
                Logging.TrySendToEngineStatsDatabase(this.Name, string.Format("WARNING:DetermineSqsQueueNameToUseBasedOnEventName(): {0}", msg), 0, TimeSpan.FromSeconds(0));
            }

            return (sqsQueueName);
        }

        /// <summary>
        /// Deletes an entry from table
        /// </summary>
        /// <param name="tableName">Table name</param>
        /// <param name="key">Key</param>
        /// <param name="rangeKey">RangeKey</param>
        /// <param name="jobId">JobId</param>
        /// <returns>Result</returns>
        private string DeleteEntry(string tableName, string key, string rangeKey, string jobId)
        {
            string deleteResult = string.Empty;
            IDictionary<string, string> deletedItemAttributes = null;
            Boolean debugCaptureDeleteFutureEvents = true;

            if (string.IsNullOrWhiteSpace(jobId))
                jobId = null;

            DateTime startFutureEventDeleteTimestamp = DateTime.UtcNow;

            if (!ReadOnly) // this is for Testing
                deleteResult = AwsFutureEventsDynamoAuroraMigration.DynamoCache(OperatingEnvironment.Current).DeleteEntity(tableName, jobId, key, rangeKey, out deletedItemAttributes);

            if (!deleteResult.Contains("Error") && deletedItemAttributes != null)
            {
                if (debugCaptureDeleteFutureEvents)
                {
                    string msg = string.Format("DeleteEntity:Success:tableName={0}:Key={1}:RangeKey={2}:JobId={3}", tableName, key, rangeKey, jobId ?? "");
                    Logging.TrySendToEngineStatsDatabase(this.Name, msg, 1, (DateTime.UtcNow - startFutureEventDeleteTimestamp));
                }
                return string.Empty;
            }
            else
            {
                if (debugCaptureDeleteFutureEvents)
                {
                    IDictionary<string, string> additionalArgs = new Dictionary<string, string>();
                    additionalArgs.Add("DeleteResult", deleteResult);
                    string msg = string.Format("DeleteEntity:Failed:tableName={0}:Key={1}:RangeKey={2}:JobId={3}", tableName, key, rangeKey, jobId ?? "");
                    Logging.TrySendToEngineStatsDatabase(this.Name, msg, 1, (DateTime.UtcNow - startFutureEventDeleteTimestamp), additionalArgs);
                }
                return deleteResult;
            }

        }

        /// <summary>
        /// Fetch a list of active customers from CloudCoordinator.
        /// Fetches all active customers if no customer name is given
        /// otherwise just the customer specified.
        /// </summary>
        /// <param name="customerName">CustomerName</param>
        /// <returns>List of customer names</returns>
        private List<string> GetActiveCustomerList(string customerName = "")
        {
            List<string> ret = new List<string>();

            CustomerFactory.TheFactory.RefreshAllInstances();

            // Get a lits of all existing active customer.
            foreach (KeyValuePair<string, ICustomer> kvp in CustomerFactory.TheFactory.GetActiveInstances())
            {
                string customer = kvp.Value.Name;
                if (!string.IsNullOrWhiteSpace(customerName))
                {
                    if (customerName.Replace(" ", "-") == customer.Replace(" ", "-"))
                        ret.Add(customer.Replace(" ", "-"));
                }
                else
                    ret.Add(customer.Replace(" ", "-"));
            }
            return ret;
        }

        #endregion - Helpers


        #region SelfDescribing Information
        public override Dictionary<string, string> InputList()
		{
			Dictionary<string, string> inputList = new Dictionary<string, string>();
			inputList.Add("# -----------------------------------------------------------1", "");
			inputList.Add("System.FutureEvents DynamoDB Table", "Optional");

			return inputList;
		}
		public override List<string> ProcessList()
		{
			List<string> processList = new List<string>();
			processList.Add("# -----------------------------------------------------------");
			processList.Add("# Work Processing");
			processList.Add("# -----------------------------------------------------------");
			processList.Add("#  Reads for Work in System.FutureEvents DynamoDB Table");
			processList.Add("#    Scan on 'EvaluateTime' Less Than or Equal to NOW DateTime");
			processList.Add("#    Deletes the Work from the System.FutureEvents DynamoDB Table");
			processList.Add("#    Writes the Work to QueuedEvents using SQS");
			return processList;
		}
		public override List<string> OutputList()
		{
			List<string> outputList = new List<string>();

			outputList.Add("# -----------------------------------------------------------");
			outputList.Add("# Write to SQS QueuedEvents of the Work Type");
			outputList.Add("# -----------------------------------------------------------");

			return outputList;
		}
		#endregion SelfDescribing Information

		#region SelfTesting Area
		public override ParameterDetailList SelfTestParameters()
		{
            string time = DateTime.UtcNow.ToString("o");
            ParameterDetail[] parameterDetails = new ParameterDetail[]
			{
				new ParameterDetail("void", "Date", time),
				new ParameterDetail("void", "Rate", "1"),
				new ParameterDetail("void", "environment", "production.sqa"),
                new ParameterDetail("void", "processor", "trollfutureevents"),
            };
			return new ParameterDetailList(parameterDetails);
		}
		public override void SelfTestExecutionArguments()
		{
			ReadOnly = false;
			LoopUpTable.EnvironmentAndProcess = new Dictionary<string, string>() { { "Environment", this.ExecuteClass.OptionalParameters["environment"].Value }, { "Processor", this.ExecuteClass.OptionalParameters["processor"].Value } };

			ScheduledWorkRequestsConfig.WorkType = "TrollFutureEventsV2";
			ScheduledWorkRequestsConfig.ItemsToFetch = Convert.ToInt32(this.ExecuteClass.OptionalParameters["Rate"].Value);
			ScheduledWorkRequestsConfig.VisibilityTimeout = 20;
			ScheduledWorkRequestsConfig.RetryAttempts = 0;
			ScheduledWorkRequestsConfig.RetrySeconds = 6000000;

            //IDictionary<string,string> evenrPriorities = GetEventPriorities();
            //foreach (EventTypes thisEvent in Enum.GetValues(typeof(EventTypes)))
            //{
            //    string sqsQueueName = DetermineSqsQueueNameToUseBasedOnEventName(thisEvent, evenrPriorities);
            //    Logging.Log(string.Format("EventType: {0}, sqsQueueName: {1}", thisEvent.ToString(), sqsQueueName), 1);
            //}

            //string tableName = string.Format("{0}.{1}.{2}.{3}",
            //        "ESI",
            //        "ESSRXMED_SCR_RET",
            //        "ESSRXMED_SCR_RET_ADULT_SP",
            //        "Jobs");

            //IDictionary<string, string> JobEntry = S3Requests.GetCustomerRow("ESI", tableName, "-1967408355598622879");
            //CalllJob.Delete_From_CallJob(ReadOnly, this.Name, JobEntry);

            return;
		}
		#endregion SelfTesting Area
	}
}

