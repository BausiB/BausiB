using CallFlow.CFD;
using CallFlow;
using MimeKit;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using System.Threading;
using System;
using TCX.Configuration;

namespace VIPCustomer
{
    public class Main : ScriptBase<Main>, ICallflow, ICallflowProcessor
    {
        private bool executionStarted;
        private bool executionFinished;

        private BufferBlock<AbsEvent> eventBuffer;

        private int currentComponentIndex;
        private List<AbsComponent> mainFlowComponentList;
        private List<AbsComponent> disconnectFlowComponentList;
        private List<AbsComponent> errorFlowComponentList;
        private List<AbsComponent> currentFlowComponentList;

        private LogFormatter logFormatter;
        private TimerManager timerManager;
        private Dictionary<string, Variable> variableMap;
        private TempWavFileManager tempWavFileManager;
        private PromptQueue promptQueue;

        private void DisconnectCallAndExitCallflow()
        {
            logFormatter.Trace("Callflow finished, disconnecting call...");
            MyCall.Terminate();
        }

        private async Task ExecuteErrorFlow()
        {
            if (currentFlowComponentList == errorFlowComponentList)
            {
                logFormatter.Trace("Error during error handler flow, exiting callflow...");
                DisconnectCallAndExitCallflow();
            }
            else
            {
                currentFlowComponentList = errorFlowComponentList;
                currentComponentIndex = 0;
                if (errorFlowComponentList.Count > 0)
                {
                    logFormatter.Trace("Start executing error handler flow...");
                    await ProcessStart();
                }
                else
                {
                    logFormatter.Trace("Error handler flow is empty...");
                    DisconnectCallAndExitCallflow();
                }
            }
        }

        private EventResults CheckEventResult(EventResults eventResult)
        {
            if (eventResult == EventResults.MoveToNextComponent && ++currentComponentIndex == currentFlowComponentList.Count)
            {
                DisconnectCallAndExitCallflow();
                return EventResults.Exit;
            }
            else if (eventResult == EventResults.Exit)
                DisconnectCallAndExitCallflow();

            return eventResult;
        }

        private void InitializeVariables(string callID)
        {
            // Call variables
            variableMap["session.ani"] = new Variable(MyCall.Caller.CallerID);
            variableMap["session.callid"] = new Variable(callID);
            variableMap["session.dnis"] = new Variable(MyCall.DN.Number);
            variableMap["session.did"] = new Variable(MyCall.Caller.CalledNumber);
            variableMap["session.audioFolder"] = new Variable(Path.Combine(RecordingManager.Instance.AudioFolder, promptQueue.ProjectAudioFolder));
            variableMap["session.transferingExtension"] = new Variable(MyCall["onbehlfof"] ?? string.Empty);

            // Standard variables
            variableMap["RecordResult.NothingRecorded"] = new Variable(RecordComponent.RecordResults.NothingRecorded);
            variableMap["RecordResult.StopDigit"] = new Variable(RecordComponent.RecordResults.StopDigit);
            variableMap["RecordResult.Completed"] = new Variable(RecordComponent.RecordResults.Completed);
            variableMap["MenuResult.Timeout"] = new Variable(MenuComponent.MenuResults.Timeout);
            variableMap["MenuResult.InvalidOption"] = new Variable(MenuComponent.MenuResults.InvalidOption);
            variableMap["MenuResult.ValidOption"] = new Variable(MenuComponent.MenuResults.ValidOption);
            variableMap["UserInputResult.Timeout"] = new Variable(UserInputComponent.UserInputResults.Timeout);
            variableMap["UserInputResult.InvalidDigits"] = new Variable(UserInputComponent.UserInputResults.InvalidDigits);
            variableMap["UserInputResult.ValidDigits"] = new Variable(UserInputComponent.UserInputResults.ValidDigits);

            // User variables
            variableMap["project$.VIP"] = new Variable("550");
            variableMap["callflow$.CustomerID"] = new Variable("");
            variableMap["RecordResult.NothingRecorded"] = new Variable(RecordComponent.RecordResults.NothingRecorded);
            variableMap["RecordResult.StopDigit"] = new Variable(RecordComponent.RecordResults.StopDigit);
            variableMap["RecordResult.Completed"] = new Variable(RecordComponent.RecordResults.Completed);
            variableMap["MenuResult.Timeout"] = new Variable(MenuComponent.MenuResults.Timeout);
            variableMap["MenuResult.InvalidOption"] = new Variable(MenuComponent.MenuResults.InvalidOption);
            variableMap["MenuResult.ValidOption"] = new Variable(MenuComponent.MenuResults.ValidOption);
            variableMap["UserInputResult.Timeout"] = new Variable(UserInputComponent.UserInputResults.Timeout);
            variableMap["UserInputResult.InvalidDigits"] = new Variable(UserInputComponent.UserInputResults.InvalidDigits);
            variableMap["UserInputResult.ValidDigits"] = new Variable(UserInputComponent.UserInputResults.ValidDigits);
            
        }

        private void InitializeComponents(ICallflow callflow, ICall myCall, string logHeader)
        {
            {
            ConditionalComponent Timecheck = new ConditionalComponent("Timecheck", callflow, myCall, logHeader);
            mainFlowComponentList.Add(Timecheck);
            Timecheck.ConditionList.Add(() => { return Convert.ToBoolean(((DateTime.Now.DayOfWeek == DayOfWeek.Monday || DateTime.Now.DayOfWeek == DayOfWeek.Tuesday || DateTime.Now.DayOfWeek == DayOfWeek.Wednesday || DateTime.Now.DayOfWeek == DayOfWeek.Thursday || DateTime.Now.DayOfWeek == DayOfWeek.Friday) && (DateTime.Now.Hour > 9 || DateTime.Now.Hour == 9 && DateTime.Now.Minute >= 0) && (DateTime.Now.Hour < 12 || DateTime.Now.Hour == 12 && DateTime.Now.Minute <= 15) || (DateTime.Now.DayOfWeek == DayOfWeek.Monday || DateTime.Now.DayOfWeek == DayOfWeek.Tuesday || DateTime.Now.DayOfWeek == DayOfWeek.Wednesday || DateTime.Now.DayOfWeek == DayOfWeek.Thursday || DateTime.Now.DayOfWeek == DayOfWeek.Friday) && (DateTime.Now.Hour > 13 || DateTime.Now.Hour == 13 && DateTime.Now.Minute >= 0) && (DateTime.Now.Hour < 17 || DateTime.Now.Hour == 17 && DateTime.Now.Minute <= 30))); });
            Timecheck.ContainerList.Add(new SequenceContainerComponent("Timecheck_0", callflow, myCall, logHeader));
            VariableAssignmentComponent RetrieveCaller = new VariableAssignmentComponent("RetrieveCaller", callflow, myCall, logHeader);
            RetrieveCaller.VariableName = "callflow$.CustomerID";
            RetrieveCaller.VariableValueHandler = () => { return variableMap["session.ani"].Value; };
            Timecheck.ContainerList[0].ComponentList.Add(RetrieveCaller);
            CustomerValidator validateData = new CustomerValidator("validateData", callflow, myCall, logHeader);
            validateData.CustomerIDSetter = () => { return variableMap["callflow$.CustomerID"].Value; };
            Timecheck.ContainerList[0].ComponentList.Add(validateData);
            ConditionalComponent checkValidationResult = new ConditionalComponent("checkValidationResult", callflow, myCall, logHeader);
            Timecheck.ContainerList[0].ComponentList.Add(checkValidationResult);
            checkValidationResult.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(validateData.ValidationResult,true)); });
            checkValidationResult.ContainerList.Add(new SequenceContainerComponent("checkValidationResult_0", callflow, myCall, logHeader));
            TransferComponent Transfer_CEL = new TransferComponent("Transfer_CEL", callflow, myCall, logHeader);
            Transfer_CEL.DestinationHandler = () => { return Convert.ToString(variableMap["project$.VIP"].Value); };
            checkValidationResult.ContainerList[0].ComponentList.Add(Transfer_CEL);
            checkValidationResult.ConditionList.Add(() => { return Convert.ToBoolean(true); });
            checkValidationResult.ContainerList.Add(new SequenceContainerComponent("checkValidationResult_1", callflow, myCall, logHeader));
            TransferComponent Accueil = new TransferComponent("Accueil", callflow, myCall, logHeader);
            Accueil.DestinationHandler = () => { return Convert.ToString(variableMap["project$.Accueil"].Value); };
            checkValidationResult.ContainerList[1].ComponentList.Add(Accueil);
            Timecheck.ConditionList.Add(() => { return Convert.ToBoolean(((DateTime.Now.DayOfWeek == DayOfWeek.Monday || DateTime.Now.DayOfWeek == DayOfWeek.Tuesday || DateTime.Now.DayOfWeek == DayOfWeek.Wednesday || DateTime.Now.DayOfWeek == DayOfWeek.Thursday || DateTime.Now.DayOfWeek == DayOfWeek.Friday) && (DateTime.Now.Hour > 17 || DateTime.Now.Hour == 17 && DateTime.Now.Minute >= 30) && (DateTime.Now.Hour < 23 || DateTime.Now.Hour == 23 && DateTime.Now.Minute <= 59) || (DateTime.Now.DayOfWeek == DayOfWeek.Monday || DateTime.Now.DayOfWeek == DayOfWeek.Tuesday || DateTime.Now.DayOfWeek == DayOfWeek.Wednesday || DateTime.Now.DayOfWeek == DayOfWeek.Thursday || DateTime.Now.DayOfWeek == DayOfWeek.Friday) && (DateTime.Now.Hour > 0 || DateTime.Now.Hour == 0 && DateTime.Now.Minute >= 0) && (DateTime.Now.Hour < 9 || DateTime.Now.Hour == 9 && DateTime.Now.Minute <= 0) || (DateTime.Now.DayOfWeek == DayOfWeek.Sunday || DateTime.Now.DayOfWeek == DayOfWeek.Saturday) && (DateTime.Now.Hour > 0 || DateTime.Now.Hour == 0 && DateTime.Now.Minute >= 0) && (DateTime.Now.Hour < 23 || DateTime.Now.Hour == 23 && DateTime.Now.Minute <= 59))); });
            Timecheck.ContainerList.Add(new SequenceContainerComponent("Timecheck_1", callflow, myCall, logHeader));
            DisconnectCallComponent DisconnectCall = new DisconnectCallComponent("DisconnectCall", callflow, myCall, logHeader);
            Timecheck.ContainerList[1].ComponentList.Add(DisconnectCall);
            }
            {
            }
            {
            }
            

            // Add a final DisconnectCall component to the main and error handler flows, in order to complete pending prompt playbacks...
            DisconnectCallComponent mainAutoAddedFinalDisconnectCall = new DisconnectCallComponent("mainAutoAddedFinalDisconnectCall", callflow, myCall, logHeader);
            DisconnectCallComponent errorHandlerAutoAddedFinalDisconnectCall = new DisconnectCallComponent("errorHandlerAutoAddedFinalDisconnectCall", callflow, myCall, logHeader);
            mainFlowComponentList.Add(mainAutoAddedFinalDisconnectCall);
            errorFlowComponentList.Add(errorHandlerAutoAddedFinalDisconnectCall);
        }

        private bool IsServerOfficeHourActive(ICall myCall)
        {
            DateTime nowDt = DateTime.Now;
            Tenant tenant = myCall.PS.GetTenant();
            if (tenant == null || tenant.IsHoliday(new DateTimeOffset(nowDt)))
                return false;

            Schedule officeHours = tenant.Hours;
            Nullable<bool> result = officeHours.IsActiveTime(nowDt);
            return result.GetValueOrDefault(false);
        }

        public Main()
        {
            this.executionStarted = false;
            this.executionFinished = false;

            this.eventBuffer = new BufferBlock<AbsEvent>();

            this.currentComponentIndex = 0;
            this.mainFlowComponentList = new List<AbsComponent>();
            this.disconnectFlowComponentList = new List<AbsComponent>();
            this.errorFlowComponentList = new List<AbsComponent>();
            this.currentFlowComponentList = mainFlowComponentList;

            this.timerManager = new TimerManager();
            this.timerManager.OnTimeout += (state) => eventBuffer.Post(new TimeoutEvent(state));
            this.variableMap = new Dictionary<string, Variable>();
        }

        public override void Start()
        {
            string callID = MyCall?.Caller["chid"] ?? "Unknown";
            string logHeader = $"VIPCustomer - CallID {callID}";
            this.logFormatter = new LogFormatter(MyCall, logHeader, "Callflow");
            this.promptQueue = new PromptQueue(this, MyCall, "VIPCustomer", logHeader);
            this.tempWavFileManager = new TempWavFileManager(logFormatter);

            InitializeComponents(this, MyCall, logHeader);
            InitializeVariables(callID);
            
            MyCall.SetBackgroundAudio(false, new string[] { });
            MyCall.OnTerminated += () => eventBuffer.Post(new CallTerminatedEvent());
            MyCall.OnDTMFInput += x => eventBuffer.Post(new DTMFReceivedEvent(x));

            logFormatter.Trace("Start executing main flow...");
            eventBuffer.Post(new StartEvent());
            Task.Run(() => EventProcessingLoop());

            
        }
        
        public void PostStartEvent()
        {
            eventBuffer.Post(new StartEvent());
        }

        public void PostDTMFReceivedEvent(char digit)
        {
            eventBuffer.Post(new DTMFReceivedEvent(digit));
        }

        public void PostPromptPlayedEvent()
        {
            eventBuffer.Post(new PromptPlayedEvent());
        }

        public void PostTransferFailedEvent()
        {
            eventBuffer.Post(new TransferFailedEvent());
        }

        public void PostMakeCallResultEvent(bool result)
        {
            eventBuffer.Post(new MakeCallResultEvent(result));
        }

        public void PostCallTerminatedEvent()
       {
            eventBuffer.Post(new CallTerminatedEvent());
        }

        public void PostTimeoutEvent(object state)
        {
            eventBuffer.Post(new TimeoutEvent(state));
        }

        private async Task EventProcessingLoop()
        {
            executionStarted = true;
            while (!executionFinished)
            {
                AbsEvent evt = await eventBuffer.ReceiveAsync();
                await evt?.ProcessEvent(this);
            }
        }
        
        public async Task ProcessStart()
        {
            try
            {
                EventResults eventResult;
                do
                {
                    AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                    logFormatter.Trace("Start executing component '" + currentComponent.Name + "'");
                    eventResult = await currentComponent.Start(timerManager, variableMap, tempWavFileManager, promptQueue);
                }
                while (CheckEventResult(eventResult) == EventResults.MoveToNextComponent);
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessDTMFReceived(char digit)
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnDTMFReceived for component '" + currentComponent.Name + "' - Digit: '" + digit + "'");
                EventResults eventResult = await currentComponent.OnDTMFReceived(timerManager, variableMap, tempWavFileManager, promptQueue, digit);
                if (CheckEventResult(eventResult) == EventResults.MoveToNextComponent)
                    await ProcessStart();
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessPromptPlayed()
        {
            try
            {
                promptQueue.NotifyPlayFinished();
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnPromptPlayed for component '" + currentComponent.Name + "'");
                EventResults eventResult = await currentComponent.OnPromptPlayed(timerManager, variableMap, tempWavFileManager, promptQueue);
                if (CheckEventResult(eventResult) == EventResults.MoveToNextComponent)
                    await ProcessStart();
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessTransferFailed()
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnTransferFailed for component '" + currentComponent.Name + "'");
                EventResults eventResult = await currentComponent.OnTransferFailed(timerManager, variableMap, tempWavFileManager, promptQueue);
                if (CheckEventResult(eventResult) == EventResults.MoveToNextComponent)
                    await ProcessStart();
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessMakeCallResult(bool result)
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnMakeCallResult for component '" + currentComponent.Name + "' - Result: '" + result + "'");
                EventResults eventResult = await currentComponent.OnMakeCallResult(timerManager, variableMap, tempWavFileManager, promptQueue, result);
                if (CheckEventResult(eventResult) == EventResults.MoveToNextComponent)
                    await ProcessStart();
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessCallTerminated()
        {
            try
            {
                if (executionStarted)
                {
                    // First notify the current component
                    AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                    logFormatter.Trace("OnCallTerminated for component '" + currentComponent.Name + "'");
                    await currentComponent.OnCallTerminated(timerManager, variableMap, tempWavFileManager, promptQueue);

                    // Next, execute disconnect flow
                    currentFlowComponentList = disconnectFlowComponentList;
                    logFormatter.Trace("Start executing disconnect handler flow...");
                    foreach (AbsComponent component in disconnectFlowComponentList)
                    {
                        logFormatter.Trace("Start executing component '" + component.Name + "'");
                        await component.Start(timerManager, variableMap, tempWavFileManager, promptQueue);
                    }
                }
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
            finally
            {
                // Finally, delete temporary files
                tempWavFileManager.DeleteFilesAndFolders();
                executionFinished = true;
            }
        }

        public async Task ProcessTimeout(object state)
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnTimeout for component '" + currentComponent.Name + "'");
                EventResults eventResult = await currentComponent.OnTimeout(timerManager, variableMap, tempWavFileManager, promptQueue, state);
                if (CheckEventResult(eventResult) == EventResults.MoveToNextComponent)
                    await ProcessStart();
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }


        public class TcxSetExtensionStatusTempComponent : AbsComponent
        {
            private StringExpressionHandler extensionHandler = null;
            private StringExpressionHandler profileNameHandler = null;

            public TcxSetExtensionStatusTempComponent(string name, ICallflow callflow, ICall myCall, string logHeader) : base(name, callflow, myCall, logHeader)
            {
            }

            public string Extension { get; set; } = string.Empty;
            public string ProfileName { get; set; } = string.Empty;

            public StringExpressionHandler ExtensionHandler
            {
                set { extensionHandler = value; }
            }

            public StringExpressionHandler ProfileNameHandler
            {
                set { profileNameHandler = value; }
            }

            private EventResults ExecuteStart()
            {
                if (extensionHandler != null) Extension = extensionHandler();
                if (profileNameHandler != null) ProfileName = profileNameHandler();

                using (DN dn = myCall.PS.GetDNByNumber(Extension))
                {
                    if (!(dn is Extension ext)) throw new Exception("Extension '" + Extension + "' could not be found.");

                    logFormatter.Trace("Setting status for Extension='" + Extension + "' to '" + ProfileName + "'");

                    // Activate the selected profile
                    bool profileFound = false;
                   foreach (FwdProfile profile in ext.FwdProfiles)
                    {
                        if (profile.Name == ProfileName)
                        {
                            ext.CurrentProfile = profile;
                            profileFound = true;
                            break;
                        }
                    }

                    if (profileFound)
                    {
                        // And disable override if active
                        if (ext.IsOverrideActiveNow) ext.OverrideExpiresAt = DateTime.UtcNow;
                        logFormatter.Trace("Status for Extension='" + Extension + "' updated.");
                        ext.Save();
                    }
                    else
                        logFormatter.Trace("Status for Extension='" + Extension + "' could not be updated, profile name not found.");
                }
                return EventResults.MoveToNextComponent;
            }

            public override Task<EventResults> Start(Dictionary<string, Variable> variableMap)
            {
                return Task.FromResult(ExecuteStart());
            }

            public override Task<EventResults> Start(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue)
            {
                return Task.FromResult(ExecuteStart());
            }

            public override Task<EventResults> OnDTMFReceived(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue, char digit)
            {
                return IgnoreDTMFReceivedEventWithResult(digit, EventResults.MoveToNextComponent);
            }

            public override Task<EventResults> OnPromptPlayed(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue)
            {
                return IgnoreEventWithResult("OnPromptPlayed", EventResults.MoveToNextComponent);
            }

            public override Task<EventResults> OnTransferFailed(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue)
            {
                return IgnoreEventWithResult("OnTransferFailed", EventResults.MoveToNextComponent);
            }

            public override Task<EventResults> OnMakeCallResult(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue, bool result)
            {
                return IgnoreMakeCallResult(result, EventResults.MoveToNextComponent);
            }

            public override Task<EventResults> OnCallTerminated(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue)
            {
                return ProcessEventWithResult("OnCallTerminated", EventResults.MoveToNextComponent);
            }

            public override Task<EventResults> OnTimeout(TimerManager timerManager, Dictionary<string, Variable> variableMap, TempWavFileManager tempWavFileManager, PromptQueue promptQueue, object state)
            {
                return IgnoreEventWithResult("OnTimeout", EventResults.MoveToNextComponent);
            }
        }


        
public class Validator
{
public bool ValidateCSV(string fileContent, string id)
{
  foreach (string line in fileContent.Split('\n'))
  {
    string[] lineParts = line.Trim().Split(',');
    if (lineParts.Length == 1)
    {
      string CustomerID = lineParts[0];
if (id == CustomerID)
        return true;
    }
  }
      return false;
 }
}        // ------------------------------------------------------------------------------------------------------------
        // User Defined component
        // ------------------------------------------------------------------------------------------------------------
        public class CustomerValidator : AbsUserComponent
        {
            private ObjectExpressionHandler _ValidationResultHandler = null;
            private ObjectExpressionHandler _CustomerIDHandler = null;
            

            protected override void InitializeVariables()
            {
                componentVariableMap["callflow$.ValidationResult"] = new Variable("");
                componentVariableMap["callflow$.CustomerID"] = new Variable("");
                
            }

            protected override void InitializeComponents()
            {
                Dictionary<string, Variable> variableMap = componentVariableMap;
                {
            FileManagementComponent readCSV = new FileManagementComponent("readCSV", callflow, myCall, logHeader);
            readCSV.Action = FileManagementComponent.Actions.Read;
            readCSV.FileMode = System.IO.FileMode.Open;
            readCSV.FileNameHandler = () => { return Convert.ToString("Customers.csv"); };
            readCSV.FirstLineToReadHandler = () => { return Convert.ToInt32(0); };
            readCSV.ReadToEndHandler = () => { return Convert.ToBoolean(true); };
            mainFlowComponentList.Add(readCSV);
            analyzeCSVExternalCodeExecutionComponent analyzeCSV = new analyzeCSVExternalCodeExecutionComponent("analyzeCSV", callflow, myCall, logHeader);
            analyzeCSV.Parameters.Add(new CallFlow.CFD.Parameter("fileContent", () => { return readCSV.Result; }));
            analyzeCSV.Parameters.Add(new CallFlow.CFD.Parameter("id", () => { return variableMap["callflow$.CustomerID"].Value; }));
            mainFlowComponentList.Add(analyzeCSV);
            VariableAssignmentComponent setResult = new VariableAssignmentComponent("setResult", callflow, myCall, logHeader);
            setResult.VariableName = "callflow$.ValidationResult";
            setResult.VariableValueHandler = () => { return analyzeCSV.ReturnValue; };
            mainFlowComponentList.Add(setResult);
            }
            {
            }
            {
            }
            
            }
            
            public CustomerValidator(string name, ICallflow callflow, ICall myCall, string logHeader) : base(name, callflow, myCall, logHeader)
            {
            }

            protected override void GetVariableValues()
            {
                if (_ValidationResultHandler != null) componentVariableMap["callflow$.ValidationResult"].Set(_ValidationResultHandler());
                if (_CustomerIDHandler != null) componentVariableMap["callflow$.CustomerID"].Set(_CustomerIDHandler());
                
            }
            
            public ObjectExpressionHandler ValidationResultSetter { set { _ValidationResultHandler = value; } }
            public object ValidationResult { get { return componentVariableMap["callflow$.ValidationResult"].Value; } }
            public ObjectExpressionHandler CustomerIDSetter { set { _CustomerIDHandler = value; } }
            public object CustomerID { get { return componentVariableMap["callflow$.CustomerID"].Value; } }
            

            private bool IsServerOfficeHourActive(ICall myCall)
            {
                DateTime nowDt = DateTime.Now;
                Tenant tenant = myCall.PS.GetTenant();
                if (tenant == null || tenant.IsHoliday(new DateTimeOffset(nowDt)))
                    return false;

                Schedule officeHours = tenant.Hours;
                Nullable<bool> result = officeHours.IsActiveTime(nowDt);
                return result.GetValueOrDefault(false);
            }
        }
public class analyzeCSVExternalCodeExecutionComponent : ExternalCodeExecutionComponent
            {
                public List<CallFlow.CFD.Parameter> Parameters { get; } = new List<CallFlow.CFD.Parameter>();
                public analyzeCSVExternalCodeExecutionComponent(string name, ICallflow callflow, ICall myCall, string projectName) : base(name, callflow, myCall, projectName) {}
                protected override object ExecuteCode()
                {
                    var instance = new Validator();
                    return instance.ValidateCSV(Convert.ToString(Parameters[0].Value), Convert.ToString(Parameters[1].Value));
                }
            }
            
    }
}
