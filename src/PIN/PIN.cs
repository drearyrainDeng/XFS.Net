using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using XFSNet;

namespace XFSNet.PIN
{
    public unsafe class PIN : XFSDeviceBase<WFSPINSTATUS, WFSPINCAPS>
    {

        #region Events
        public event Action GetDataComplete;
        public event Action<int> GetDataError;
        public event Action<string> PINKey;
        #endregion

        public PIN()
        {
            commandHandlers = new Dictionary<int, XFSCommandHandler>();
            eventHandlers = new Dictionary<int, XFSEventHandler>();
            
            commandHandlers.Add(PINDefinition.WFS_CMD_PIN_GET_DATA, new XFSCommandHandler(OnGetDataError, OnGetDataComplete));
            eventHandlers.Add(PINDefinition.WFS_EXEE_PIN_KEY, new XFSEventHandler(null, OnPINKey));
        }

        public void GetData(ushort maxLen, bool autoEnd, XFSPINKey activeKeys, XFSPINKey terminateKeys, XFSPINKey activeFDKs = XFSPINKey.WFS_PIN_FK_UNUSED,
            XFSPINKey terminateFDKs = XFSPINKey.WFS_PIN_FK_UNUSED)
        {
            WFSPINGETDATA inputData = new WFSPINGETDATA();
            inputData.usMaxLen = maxLen;
            inputData.bAutoEnd = autoEnd;
            inputData.ulActiveFDKs = activeFDKs;
            inputData.ulActiveKeys = activeKeys;
            inputData.ulTerminateFDKs = terminateFDKs;
            inputData.ulTerminateKeys = terminateKeys;
            int len = Marshal.SizeOf(typeof(WFSPINGETDATA));
            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr(inputData, ptr, false);
            ExecuteCommand(PINDefinition.WFS_CMD_PIN_GET_DATA, ptr, OnGetDataError);
        }
        protected virtual void OnGetDataComplete()
        {
            if (GetDataComplete != null)
                GetDataComplete();
        }

        protected virtual void OnGetDataError(string service, int code, string message)
        {
            if (GetDataError != null)
                GetDataError(code);
        }

        protected virtual void OnPINKey(IntPtr ptr)
        {
            WFSPINKEY key = new WFSPINKEY();
            XFSUtil.PtrToStructure(ptr, ref key);

            if (PINKey != null)
                PINKey(key.ulDigit.ToString().Substring(11));
        }
        #region Virtual
        protected override int StatusCommandCode
        {
            get
            {
                return PINDefinition.WFS_INF_PIN_STATUS;
            }
        }
        protected override int CapabilityCommandCode
        {
            get
            {
                return PINDefinition.WFS_INF_PIN_CAPABILITIES;
            }
        }
        #endregion
    }
}
