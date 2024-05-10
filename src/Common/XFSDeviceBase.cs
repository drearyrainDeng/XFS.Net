using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace XFSNet
{
    public abstract class XFSDeviceBase<TStatusType, TCapabilityType> : UserControl
    {
        #region Events
        public event Action OpenComplete;
        public event Action<int> OpenError;
        public event Action CloseComplete;
        public event Action RegisterComplete;
        public event Action<int> RegisterError;
        #endregion

        #region Fields
        protected ushort hService;
        public int HService
        {
            get
            {
                return hService;
            }
        }
        protected string serviceName;
        public string ServiceName
        {
            get
            {
                return serviceName;
            }
        }
        protected bool autoRegister = false;
        protected int requestID = 0;
        protected bool isStartup = false;
        /// <summary>
        /// timeout of excution
        /// </summary>
        public int TimeOut { get; set; }
        /// <summary>
        /// dulication of handle for crossing thread
        /// </summary>
        protected IntPtr MessageHandle;
        protected abstract int StatusCommandCode { get; }
        protected abstract int CapabilityCommandCode { get; }
        protected Dictionary<int, XFSEventHandler> eventHandlers;
        protected Dictionary<int, XFSCommandHandler> commandHandlers;
        #endregion

        public XFSDeviceBase()
        {
            this.Width = this.Height = 0;
            this.Visible = false;
            MessageHandle = Handle;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg >= XFSDefinition.WFS_OPEN_COMPLETE &&
                m.Msg <= XFSDefinition.WFS_SYSTEM_EVENT)
            {
                WFSRESULT result = new WFSRESULT();
                if (m.LParam != IntPtr.Zero)
                    XFSUtil.PtrToStructure(m.LParam, ref result);
                switch (m.Msg)
                {
                    case XFSDefinition.WFS_OPEN_COMPLETE:
                        OnOpenComplete();
                        break;
                    case XFSDefinition.WFS_CLOSE_COMPLETE:
                        OnCloseComplete();
                        break;
                    case XFSDefinition.WFS_REGISTER_COMPLETE:
                        OnRegisterComplete();
                        break;
                    case XFSDefinition.WFS_EXECUTE_COMPLETE:
                        OnExecuteComplete(ref result);
                        break;
                    case XFSDefinition.WFS_EXECUTE_EVENT:
                    case XFSDefinition.WFS_SERVICE_EVENT:
                    case XFSDefinition.WFS_USER_EVENT:
                    case XFSDefinition.WFS_SYSTEM_EVENT:
                        if (eventHandlers.ContainsKey(result.dwCommandCodeOrEventID))
                        {
                            var temp = eventHandlers[result.dwCommandCodeOrEventID];
                            if (temp.EventHandler != null)
                            {
                                temp.EventHandler();
                            }
                            else if (temp.SubRoutine != null)
                            {
                                temp.SubRoutine(result.lpBuffer);
                            }
                        }
                        break;
                }
                XfsApi.WFSFreeResult(ref result);
            }
            else
                base.WndProc(ref m);
        }

        protected virtual void OnOpenComplete()
        {
            if (OpenComplete != null)
                OpenComplete();
            if (autoRegister)
            {
                InnerRegister(GetEventClass());
            }
        }

        protected virtual bool InnerGetInfo<T>(int category, IntPtr inParam, out T value)
        {
            IntPtr pOutParam = IntPtr.Zero;
            value = default(T);
            int hResult = XfsApi.WFSGetInfo(hService, category, inParam, TimeOut, ref pOutParam);
            if (hResult == XFSDefinition.WFS_SUCCESS)
            {
                WFSRESULT wfsResult = (WFSRESULT)Marshal.PtrToStructure(pOutParam, typeof(WFSRESULT));
                if (wfsResult.hResult == XFSDefinition.WFS_SUCCESS)
                {
                    value = (T)Marshal.PtrToStructure(wfsResult.lpBuffer, typeof(T));
                    return true;
                }
            }
            XfsApi.WFSFreeResult(pOutParam);
            return false;
        }
        protected void InnerRegister(int eventClasses)
        {
            int hResult = XfsApi.WFSAsyncRegister(hService, eventClasses, MessageHandle
                , MessageHandle, ref requestID);
            if (hResult != XFSDefinition.WFS_SUCCESS)
            {
                OnRegisterError(hResult);
            }
        }
        protected virtual int GetEventClass()
        {
            return XFSDefinition.EXECUTE_EVENTS | XFSDefinition.SERVICE_EVENTS
                | XFSDefinition.SYSTEM_EVENTS | XFSDefinition.USER_EVENTS;
        }
        protected virtual void OnOpenError(int code)
        {
            if (OpenError != null)
                OpenError(code);
        }
        protected virtual void OnCloseComplete()
        {
            if (CloseComplete != null)
                CloseComplete();
        }
        protected virtual void OnRegisterComplete()
        {
            if (RegisterComplete != null)
                RegisterComplete();
        }
        protected virtual void OnRegisterError(int code)
        {
            if (RegisterError != null)
                RegisterError(code);
        }
        protected virtual void OnExecuteComplete(ref WFSRESULT result)
        {
            if (commandHandlers.ContainsKey(result.dwCommandCodeOrEventID))
            {
                XFSCommandHandler handler = commandHandlers[result.dwCommandCodeOrEventID];
                if (result.hResult == XFSDefinition.WFS_SUCCESS)
                {
                    if (handler.EventHandler != null)
                    {
                        handler.EventHandler();
                    }
                    else if (handler.SubRoutine != null)
                    {
                        handler.SubRoutine(result.lpBuffer);
                    }
                }
                else
                {
                    if (handler.ErrorHandler != null)
                    {
                        handler.ErrorHandler(serviceName, result.hResult, string.Empty);
                    }
                }
            }
        }
        protected virtual void OnExecuteEvent(ref WFSRESULT result)
        { }
        protected virtual void OnServiceEvent(ref WFSRESULT result)
        { }
        protected virtual void OnUserEvent(ref WFSRESULT result)
        { }
        protected virtual void OnSystemEvent(ref WFSRESULT result)
        { }
        public void Open(string logicName, bool paramAutoRegister = true,
            string appID = "XFS.NET", string lowVersion = "3.0",
            string highVersion = "3.0")
        {
            serviceName = logicName;
            autoRegister = paramAutoRegister;
            int requestVersion = XFSUtil.ParseVersionString(lowVersion,
                highVersion);
            WFSVERSION srvcVersion = new WFSVERSION();
            WFSVERSION spVersion = new WFSVERSION();
            int hResult = 0;
            if (!isStartup)
            {
                hResult = XfsApi.WFSStartUp(requestVersion, ref spVersion);
                if (hResult != XFSDefinition.WFS_SUCCESS &&
                    hResult != XFSDefinition.WFS_ERR_ALREADY_STARTED)
                {
                    OnOpenError(hResult);
                    return;
                }
            }
            hResult = XfsApi.WFSAsyncOpen(logicName, IntPtr.Zero, appID, 0,
                XFSConstants.WFS_INDEFINITE_WAIT, ref hService,
            MessageHandle, requestVersion, ref srvcVersion, ref spVersion,
            ref requestID);
            if (hResult != XFSDefinition.WFS_SUCCESS)
            {
                OnOpenError(hResult);
            }
        }
        public void Register(int eventClasses = XFSDefinition.EXECUTE_EVENTS |
            XFSDefinition.SERVICE_EVENTS | XFSDefinition.SYSTEM_EVENTS |
            XFSDefinition.USER_EVENTS)
        {
            InnerRegister(eventClasses);
        }
        public bool GetStatus(out TStatusType value)
        {
            return InnerGetInfo(StatusCommandCode, IntPtr.Zero, out value);
        }
        public bool GetCapability(out TCapabilityType value)
        {
            return InnerGetInfo(CapabilityCommandCode, IntPtr.Zero, out value);
        }
        public void Close()
        {
            //
        }

        public void Reset()
        {
            //
        }
        public void Cancel()
        {
            XfsApi.WFSCancelAsyncRequest(hService, requestID);
        }
        protected void HandleExecutionResult(int hResult, Action completeHandler, Action<string, int, string> errorHandler)
        {
            if (hResult == XFSDefinition.WFS_SUCCESS)
                completeHandler();
            else
                errorHandler(serviceName, hResult, hResult.ToString());
        }
        protected void HandleAysncExcutionResult(int hResult, Action<string, int, string> errorHandler)
        {
            if (hResult != XFSDefinition.WFS_SUCCESS)
                OnExecuteError(errorHandler, hResult);
        }
        protected void ExecuteCommand(int commandCode, IntPtr ptrParam, Action<string, int, string> errorHandler = null)
        {
            int hResult = XfsApi.WFSAsyncExecute(hService, commandCode, ptrParam, TimeOut, MessageHandle, ref requestID);
            Marshal.FreeHGlobal(ptrParam);
            if (hResult != XFSDefinition.WFS_SUCCESS && errorHandler != null)
                errorHandler(serviceName, hResult, string.Empty);
        }
        protected virtual void OnExecuteError(Action<string, int, string> errorHandler, int code)
        {
            if (errorHandler != null)
                errorHandler(ServiceName, code, code.ToString());
        }
    }
}
