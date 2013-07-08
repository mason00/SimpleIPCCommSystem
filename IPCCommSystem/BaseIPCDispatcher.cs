﻿using System;
using System.Linq;
using System.Threading;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting;
using System.Runtime.Serialization;
using SimpleIPCCommSystem.Resources;
using SimpleIPCCommSystem.Messages;
using SimpleIPCCommSystem.Utilities;

namespace SimpleIPCCommSystem {
    public class BaseIPCDispatcher : IIPCBaseIPCDispatcher, IDisposable {
        IIPCGUID _receaverID;
        IIPCGUID _dispatcherID;
        private EventWaitHandle _receaverWaitHandle;
        private EventWaitHandle _dispatcherWaitHandle;
        private IPCBaseMessagesQueue _receaverQueue;

        private static IpcClientChannel channel = new IpcClientChannel();

        public BaseIPCDispatcher(IIPCGUID receaverID) {
            // create receaver wait handle
            _receaverID = receaverID;
            _receaverWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset,
                _receaverID.Value);

            // create dispatcher wait handle
            _dispatcherID = new IPCGUID();
            _dispatcherWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset,
                _dispatcherID.Value);

            if (!ChannelServices.RegisteredChannels.Any(i => i == channel)) {
                ChannelServices.RegisterChannel(channel, true);
            }
        }

        public IPCDispatchResult Dispatch(IIPCBaseMessage message) {

            string receaverUri =
                String.Format(CommonResource.BaseUri, _receaverID.Value, IPCBaseMessagesQueue.URISuffix);

            _receaverQueue = (IPCBaseMessagesQueue)Activator.GetObject(typeof(IPCBaseMessagesQueue),
                receaverUri);
            message.SenderID = _dispatcherID;
            try {
                if (RemotingServices.IsTransparentProxy(_receaverQueue)) {
                    switch (message.MessageType) {
                        case IPCDispatchType.Async:
                            return DoDispatchAsyncMessage(message as IPCBaseAsyncMessage, _receaverQueue);
                        case IPCDispatchType.Sync:
                            return DoDispatchSyncMessage(message as IPCBaseSyncMessage, _receaverQueue);
                        default:
                            break;
                    }
                }
            } catch (Exception ex) {
                if (ex is RemotingException || ex is SerializationException) {
                    return IPCDispatchResult.Fail;
                }
                throw;
            }
            return IPCDispatchResult.Success;
        }

        protected IPCDispatchResult DoDispatchAsyncMessage(IPCBaseAsyncMessage message, IPCBaseMessagesQueue receaverQueue) {
            if (message == null || receaverQueue == null) {
                return IPCDispatchResult.Fail;
            }
            _receaverQueue.EnqueueMessage(message);
            _receaverWaitHandle.Set();
            return IPCDispatchResult.Success;
        }

        protected IPCDispatchResult DoDispatchSyncMessage(IPCBaseSyncMessage message, IPCBaseMessagesQueue receaverQueue) {
            if (message == null || receaverQueue == null) {
                return IPCDispatchResult.Fail;
            }

            // share object
            ObjRef QueueRef = RemotingServices.Marshal(message,
                message.UriSuffix,
                message.GetType());

            QueueRef.URI = new IPCUri(message.SenderID, message).Value; // TODO: get rid of this code?

            // notify receaver
            IIPCGUID helperID = new IPCGUID(new Guid().ToString());
            IPCSyncHelperMessage helperMessage = new IPCSyncHelperMessage(message, helperID);
            receaverQueue.EnqueueMessage(helperMessage);
            _receaverWaitHandle.Set();

            if (!_dispatcherWaitHandle.WaitOne(message.TimeOut))
                return IPCDispatchResult.Timeout;

            return IPCDispatchResult.Success;
        }


        public IIPCGUID Receaver {
            get { return _receaverID; }
        }

        public void Dispose() {
            _receaverWaitHandle.Dispose();
            _dispatcherWaitHandle.Dispose();
        }
    }
}
