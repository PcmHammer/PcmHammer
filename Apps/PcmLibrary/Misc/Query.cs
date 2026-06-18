// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Encapsulates the code to send a message and monitor the VPW bus until a response to that message is received.
    /// </summary>
    /// <remarks>
    /// The VPW protocol allows modules to inject messages whenever they want, so
    /// unexpected messages are common. After sending a message you can't assume
    /// that the next message on the bus will be the response that you were hoping
    /// to receive.
    /// </remarks>
    public class Query<T>
    {
        /// <summary>
        /// The device to use to send the message.
        /// </summary>
        private Device device;

        /// <summary>
        /// The code that will generate the outgoing message.
        /// </summary>
        private Func<Message> generator;

        /// <summary>
        /// Code that will select the response from whatever VPW messages appear on the bus.
        /// </summary>
        private Func<Message, Response<T>> filter;
                
        /// <summary>
        /// This will indicate when the user has requested cancellation.
        /// </summary>
        private CancellationToken cancellationToken;

        /// <summary>
        /// Optionally use tool-present messages as a way of polling for slow responses.
        /// </summary>
        private ToolPresentNotifier? notifier;

        /// <summary>
        /// Provides access to the Results and Debug panes.
        /// </summary>
        private ILogger logger;

        /// <summary>
        /// Optional inbound address filter, installed on the device for the duration of
        /// Execute(). Decides which received messages are on-conversation for this
        /// exchange; unrelated bus traffic is dropped before it reaches the response
        /// selector. Null means "derive a default from the outgoing request".
        /// </summary>
        private Func<Message, Predicate<Message>>? acceptInbound;

        public int MaxTimeouts { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="acceptInbound">
        /// Optional factory that, given the outgoing request, returns the predicate used
        /// to filter inbound traffic for this exchange. When null,
        /// <see cref="MessageFilters.RepliesFrom"/> is used: it keeps only messages addressed
        /// to the tool from the module we queried. Pass a predicate that accepts everything
        /// for broadcast / multi-module exchanges.
        /// </param>
        public Query(Device device, Func<Message> generator, Func<Message, Response<T>> filter, ILogger logger, CancellationToken cancellationToken, ToolPresentNotifier? notifier = null, Func<Message, Predicate<Message>>? acceptInbound = null)
        {
            this.device = device;
            this.generator = generator;
            this.filter = filter;
            this.logger = logger;
            this.notifier = notifier;
            this.cancellationToken = cancellationToken;
            this.acceptInbound = acceptInbound;
            this.MaxTimeouts = 5;
        }

        /// <summary>
        /// Send the message, wait for the response.
        /// </summary>
        public async Task<Response<T>> Execute()
        {
            this.device.ClearMessageQueue();

            Message request = this.generator();

            // Filter inbound traffic to this conversation for as long as this exchange is
            // trying. The 'using' guarantees the filter is removed on every exit path below
            // (success, error, retry exhaustion, cancellation, or exception), so the filter
            // never outlives the attempt.
            Predicate<Message> accept = this.acceptInbound != null ? this.acceptInbound(request) : MessageFilters.RepliesFrom(request);
            using (this.device.FilterInbound(accept))
            {
            bool success = false;
            for (int sendAttempt = 1; sendAttempt <= 2; sendAttempt++)
            {
                if (this.cancellationToken.IsCancellationRequested)
                {
                    return Response.Create(ResponseStatus.Cancelled, default(T)!);
                }

                success = await this.device.SendMessage(request);

                if (!success)
                {
                    logger.AddDebugMessage("Send failed. Attempt #" + sendAttempt.ToString());
                    continue;
                }

                // We'll read up to 50 times from the queue (just to avoid 
                // looping forever) but we will but only allow two timeouts.
                int timeouts = 0;
                for (int receiveAttempt = 1; receiveAttempt <= 50; receiveAttempt++)
                {
                    if (this.cancellationToken.IsCancellationRequested)
                    {
                        return Response.Create(ResponseStatus.Cancelled, default(T)!);
                    }

                    Message received = await this.device.ReceiveMessage();

                    if (received == null)
                    {
                        timeouts++;
                        if (timeouts >= this.MaxTimeouts)
                        {
                            // Maybe try sending again if we haven't run out of send attempts.
                            logger.AddDebugMessage(
                                string.Format(
                                    "Receive timed out. Attempt #{0}, Timeout #{1}.",
                                    receiveAttempt,
                                    timeouts));
                            break;
                        }

                        if (this.notifier != null)
                        {
                            await this.notifier.ForceNotify();
                        }

                        continue;
                    }

                    Response<T> result = this.filter(received);
                    if (result.Status == ResponseStatus.Success)
                    {
                        return result;
                    }

                    if (result.Status == ResponseStatus.Error)
                    {
                        return Response.Create(ResponseStatus.Error, default(T)!);
                    }

                    logger.AddDebugMessage(
                        string.Format(
                            "Received an unexpected response. Attempt #{0}, status {1}.",
                            receiveAttempt,
                            result.Status));
                }
            }

            return Response.Create(ResponseStatus.Error, default(T)!);
            } // using (FilterInbound)
        }
    }
}
