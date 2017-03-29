//---------------------------------------------------------------------
// <copyright file="ODataBatchReader.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.Core
{
    #region Namespaces
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Text;
#if ODATALIB_ASYNC
    using System.Threading.Tasks;
#endif

    #endregion Namespaces

    /// <summary>
    /// Class for reading OData batch messages; also verifies the proper sequence of read calls on the reader.
    /// </summary>
    public abstract class ODataBatchReader : IODataBatchOperationListener
    {
        /// <summary>The input context to read the content from.</summary>
        private ODataInputContext inputContext;

        /// <summary>True if the writer was created for synchronous operation; false for asynchronous.</summary>
        private readonly bool synchronous;

        /// <summary>The batch-specific URL resolver that stores the content IDs found in a changeset and supports resolving cross-referencing URLs.</summary>
        internal readonly ODataBatchUrlResolver urlResolver;

        /// <summary>The current state of the batch reader.</summary>
        private ODataBatchReaderState batchReaderState;

        /// <summary>The current size of the batch message, i.e., how many query operations and changesets have been read.</summary>
        private uint currentBatchSize;

        /// <summary>The current size of the active changeset, i.e., how many operations have been read for the changeset.</summary>
        private uint currentChangeSetSize;

        /// <summary>An enumeration tracking the state of the current batch operation.</summary>
        private OperationState operationState;

        /// <summary>The value of the content ID header of the current part.</summary>
        /// <remarks>
        /// The content ID header of the current part should only be visible to subsequent parts
        /// so we can only add it to the URL resolver once we are done with the current part.
        /// </remarks>
        private string contentIdToAddOnNextRead;

        /// <summary>
        /// Internal switch for whether we support reading Content-ID header appear in HTTP head instead of ChangeSet head.
        /// </summary>
        private bool allowLegacyContentIdBehavior;

        protected ODataInputContext InputContext 
        {
            get { return this.inputContext; }
        }

        protected string ContentIdToAddOnNextRead
        {
            get { return this.contentIdToAddOnNextRead; }
            set { this.contentIdToAddOnNextRead = value; }
        }

        protected OperationState ReaderOperationState
        {
            get { return this.operationState; }
            set { this.operationState = value; }
        }

        protected bool AllowLegacyContentIdBehavior
        {
            get { return this.allowLegacyContentIdBehavior;}
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="inputContext">The input context to read the content from.</param>
        /// <param name="batchBoundary">The boundary string for the batch structure itself.</param>
        /// <param name="batchEncoding">The encoding to use to read from the batch stream.</param>
        /// <param name="synchronous">true if the reader is created for synchronous operation; false for asynchronous.</param>
        internal ODataBatchReader(ODataInputContext inputContext, Encoding batchEncoding, bool synchronous)
        {
            Debug.Assert(inputContext != null, "inputContext != null");

            this.inputContext = inputContext;
            this.synchronous = synchronous;
            this.urlResolver = new ODataBatchUrlResolver(inputContext.UrlResolver);

            this.allowLegacyContentIdBehavior = true;
        }

        /// <summary>
        /// An enumeration to track the state of a batch operation.
        /// </summary>
        protected enum OperationState
        {
            /// <summary>No action has been performed on the operation.</summary>
            None,

            /// <summary>The batch message for the operation has been created and returned to the caller.</summary>
            MessageCreated,

            /// <summary>The stream of the batch operation message has been requested.</summary>
            StreamRequested,

            /// <summary>The stream of the batch operation message has been disposed.</summary>
            StreamDisposed,
        }

        /// <summary>Gets the current state of the batch reader.</summary>
        /// <returns>The current state of the batch reader.</returns>
        public ODataBatchReaderState State
        {
            get
            {
                this.inputContext.VerifyNotDisposed();
                return this.batchReaderState;
            }

            set
            {
                this.batchReaderState = value;
            }
        }

        /// <summary> Reads the next part from the batch message payload. </summary>
        /// <returns>True if more items were read; otherwise false.</returns>
        public bool Read()
        {
            this.VerifyCanRead(true);
            return this.InterceptException((Func<bool>)this.ReadSynchronously);
        }

#if ODATALIB_ASYNC
        /// <summary>Asynchronously reads the next part from the batch message payload.</summary>
        /// <returns>A task that when completed indicates whether more items were read.</returns>
        [SuppressMessage("Microsoft.MSInternal", "CA908:AvoidTypesThatRequireJitCompilationInPrecompiledAssemblies", Justification = "API design calls for a bool being returned from the task here.")]
        public Task<bool> ReadAsync()
        {
            this.VerifyCanRead(false);
            return this.ReadAsynchronously().FollowOnFaultWith(t => this.State = ODataBatchReaderState.Exception);
        }
#endif

        /// <summary>Returns an <see cref="T:Microsoft.OData.Core.ODataBatchOperationRequestMessage" /> for reading the content of a batch operation.</summary>
        /// <returns>A request message for reading the content of a batch operation.</returns>
        public ODataBatchOperationRequestMessage CreateOperationRequestMessage()
        {
            this.VerifyCanCreateOperationRequestMessage(/*synchronousCall*/ true);
            return this.InterceptException((Func<ODataBatchOperationRequestMessage>)this.CreateOperationRequestMessageImplementation);
        }

#if ODATALIB_ASYNC
        /// <summary>Asynchronously returns an <see cref="T:Microsoft.OData.Core.ODataBatchOperationRequestMessage" /> for reading the content of a batch operation.</summary>
        /// <returns>A task that when completed returns a request message for reading the content of a batch operation.</returns>
        public Task<ODataBatchOperationRequestMessage> CreateOperationRequestMessageAsync()
        {
            this.VerifyCanCreateOperationRequestMessage(/*synchronousCall*/ false);
            return TaskUtils.GetTaskForSynchronousOperation<ODataBatchOperationRequestMessage>(
                this.CreateOperationRequestMessageImplementation)
                .FollowOnFaultWith(t => this.State = ODataBatchReaderState.Exception);
        }
#endif

        /// <summary>Returns an <see cref="T:Microsoft.OData.Core.ODataBatchOperationResponseMessage" /> for reading the content of a batch operation.</summary>
        /// <returns>A response message for reading the content of a batch operation.</returns>
        public ODataBatchOperationResponseMessage CreateOperationResponseMessage()
        {
            this.VerifyCanCreateOperationResponseMessage(/*synchronousCall*/ true);
            return this.InterceptException((Func<ODataBatchOperationResponseMessage>)this.CreateOperationResponseMessageImplementation);
        }

#if ODATALIB_ASYNC
        /// <summary>Asynchronously returns an <see cref="T:Microsoft.OData.Core.ODataBatchOperationResponseMessage" /> for reading the content of a batch operation.</summary>
        /// <returns>A task that when completed returns a response message for reading the content of a batch operation.</returns>
        public Task<ODataBatchOperationResponseMessage> CreateOperationResponseMessageAsync()
        {
            this.VerifyCanCreateOperationResponseMessage(/*synchronousCall*/ false);
            return TaskUtils.GetTaskForSynchronousOperation<ODataBatchOperationResponseMessage>(
                this.CreateOperationResponseMessageImplementation)
                .FollowOnFaultWith(t => this.State = ODataBatchReaderState.Exception);
        }
#endif

        /// <summary>
        /// This method is called to notify that the content stream for a batch operation has been requested.
        /// </summary>
        void IODataBatchOperationListener.BatchOperationContentStreamRequested()
        {
            this.operationState = OperationState.StreamRequested;
        }

#if ODATALIB_ASYNC
        /// <summary>
        /// This method is called to notify that the content stream for a batch operation has been requested.
        /// </summary>
        /// <returns>
        /// A task representing any action that is running as part of the status change of the reader; 
        /// null if no such action exists.
        /// </returns>
        Task IODataBatchOperationListener.BatchOperationContentStreamRequestedAsync()
        {
            this.operationState = OperationState.StreamRequested;
            return TaskUtils.CompletedTask;
        }
#endif

        /// <summary>
        /// This method is called to notify that the content stream of a batch operation has been disposed.
        /// </summary>
        void IODataBatchOperationListener.BatchOperationContentStreamDisposed()
        {
            this.operationState = OperationState.StreamDisposed;
        }

        /// <summary>
        /// Continues reading from the batch message payload.
        /// </summary>
        /// <returns>true if more items were read; otherwise false.</returns>
        protected abstract bool ReadImplementation();

        /// <summary>
        /// Returns the cached <see cref="ODataBatchOperationRequestMessage"/> for reading the content of an operation 
        /// in a batch request.
        /// </summary>
        /// <returns>The message that can be used to read the content of the batch request operation from.</returns>
        protected abstract ODataBatchOperationRequestMessage CreateOperationRequestMessageImplementation();

        /// <summary>
        /// Returns the cached <see cref="ODataBatchOperationRequestMessage"/> for reading the content of an operation 
        /// in a batch request.
        /// </summary>
        /// <returns>The message that can be used to read the content of the batch request operation from.</returns>
        protected abstract ODataBatchOperationResponseMessage CreateOperationResponseMessageImplementation();

        /// <summary>
        /// Reads the next part from the batch message payload.
        /// </summary>
        /// <returns>true if more information was read; otherwise false.</returns>
        private bool ReadSynchronously()
        {
            return this.ReadImplementation();
        }

#if ODATALIB_ASYNC
        /// <summary>
        /// Asynchronously reads the next part from the batch message payload.
        /// </summary>
        /// <returns>A task that when completed indicates whether more information was read.</returns>
        [SuppressMessage("Microsoft.MSInternal", "CA908:AvoidTypesThatRequireJitCompilationInPrecompiledAssemblies", Justification = "API design calls for a bool being returned from the task here.")]
        private Task<bool> ReadAsynchronously()
        {
            // We are reading from the fully buffered read stream here; thus it is ok
            // to use synchronous reads and then return a completed task
            // NOTE: once we switch to fully async reading this will have to change
            return TaskUtils.GetTaskForSynchronousOperation<bool>(this.ReadImplementation);
        }
#endif
        /// <summary>
        /// Parses the request line of a batch operation request, without HTTP method validation.
        /// </summary>
        /// <param name="requestLine">The request line as a string.</param>
        /// <param name="httpMethod">The parsed HTTP method of the request.</param>
        /// <param name="requestUri">The parsed <see cref="Uri"/> of the request.</param>
        internal void ParseRequestLine(string requestLine, out string httpMethod, out Uri requestUri)
        {
            ParseRequestLine(requestLine, /*httpMethodValidation*/ null, out httpMethod, out requestUri);
        }

        /// <summary>
        /// Parses the request line of a batch operation request.
        /// </summary>
        /// <param name="requestLine">The request line as a string.</param>
        /// <param name="httpMethodValidation">The validation for the HTTP method in the request line.</param>
        /// <param name="httpMethod">The parsed HTTP method of the request.</param>
        /// <param name="requestUri">The parsed <see cref="Uri"/> of the request.</param>
        internal void ParseRequestLine(string requestLine, Action<string> httpMethodValidation, out string httpMethod, out Uri requestUri)
        {
            Debug.Assert(!this.inputContext.ReadingResponse, "Must only be called for requests.");

            // Batch Request: POST /Customers HTTP/1.1
            // Since the uri can contain spaces, the only way to read the request url, is to
            // check for first space character and last space character and anything between
            // them.
            int firstSpaceIndex = requestLine.IndexOf(' ');

            // Check whether there are enough characters after the first space for the 2nd and 3rd segments 
            // (and a whitespace in between)
            if (firstSpaceIndex <= 0 || requestLine.Length - 3 <= firstSpaceIndex)
            {
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidRequestLine(requestLine));
            }

            int lastSpaceIndex = requestLine.LastIndexOf(' ');
            if (lastSpaceIndex < 0 || lastSpaceIndex - firstSpaceIndex - 1 <= 0 || requestLine.Length - 1 <= lastSpaceIndex)
            {
                // only 2 segments or empty 2nd or 3rd segments
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidRequestLine(requestLine));
            }

            httpMethod = requestLine.Substring(0, firstSpaceIndex);               // Request - Http method  
            string uriSegment = requestLine.Substring(firstSpaceIndex + 1, lastSpaceIndex - firstSpaceIndex - 1);      // Request - Request uri  
            string httpVersionSegment = requestLine.Substring(lastSpaceIndex + 1);             // Request - Http version

            // Validate HttpVersion
            if (string.CompareOrdinal(ODataConstants.HttpVersionInBatching, httpVersionSegment) != 0)
            {
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidHttpVersionSpecified(httpVersionSegment, ODataConstants.HttpVersionInBatching));
            }

            // NOTE: this method will throw if the method is not recognized.
            HttpUtils.ValidateHttpMethod(httpMethod);

            if (httpMethodValidation != null)
            {
                httpMethodValidation(httpMethod);
            }

            requestUri = new Uri(uriSegment, UriKind.RelativeOrAbsolute);
            requestUri = ODataBatchUtils.CreateOperationRequestUri(requestUri, this.inputContext.MessageReaderSettings.BaseUri, this.urlResolver);
        }

        /// <summary>
        /// Parses the response line of a batch operation response.
        /// </summary>
        /// <param name="responseLine">The response line as a string.</param>
        /// <returns>The parsed status code from the response line.</returns>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "'this' is used when built in debug")]
        internal int ParseResponseLine(string responseLine)
        {
            Debug.Assert(this.inputContext.ReadingResponse, "Must only be called for responses.");

            // Batch Response: HTTP/1.1 200 Ok
            // Since the http status code strings have spaces in them, we cannot use the same
            // logic. We need to check for the second space and anything after that is the error
            // message.
            int firstSpaceIndex = responseLine.IndexOf(' ');
            if (firstSpaceIndex <= 0 || responseLine.Length - 3 <= firstSpaceIndex)
            {
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidResponseLine(responseLine));
            }

            int secondSpaceIndex = responseLine.IndexOf(' ', firstSpaceIndex + 1);
            if (secondSpaceIndex < 0 || secondSpaceIndex - firstSpaceIndex - 1 <= 0 || responseLine.Length - 1 <= secondSpaceIndex)
            {
                // only 2 segments or empty 2nd or 3rd segments
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidResponseLine(responseLine));
            }

            string httpVersionSegment = responseLine.Substring(0, firstSpaceIndex);
            string statusCodeSegment = responseLine.Substring(firstSpaceIndex + 1, secondSpaceIndex - firstSpaceIndex - 1);

            // Validate HttpVersion
            if (string.CompareOrdinal(ODataConstants.HttpVersionInBatching, httpVersionSegment) != 0)
            {
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidHttpVersionSpecified(httpVersionSegment, ODataConstants.HttpVersionInBatching));
            }

            int intResult;
            if (!Int32.TryParse(statusCodeSegment, out intResult))
            {
                throw new ODataException(Strings.ODataBatchReaderStream_NonIntegerHttpStatusCode(statusCodeSegment));
            }

            return intResult;
        }

        /// <summary>
        /// Verifies that calling CreateOperationRequestMessage if valid.
        /// </summary>
        /// <param name="synchronousCall">true if the call is to be synchronous; false otherwise.</param>
        private void VerifyCanCreateOperationRequestMessage(bool synchronousCall)
        {
            this.VerifyReaderReady();
            this.VerifyCallAllowed(synchronousCall);

            if (this.inputContext.ReadingResponse)
            {
                this.ThrowODataException(Strings.ODataBatchReader_CannotCreateRequestOperationWhenReadingResponse);
            }

            if (this.State != ODataBatchReaderState.Operation)
            {
                this.ThrowODataException(Strings.ODataBatchReader_InvalidStateForCreateOperationRequestMessage(this.State));
            }

            if (this.operationState != OperationState.None)
            {
                this.ThrowODataException(Strings.ODataBatchReader_OperationRequestMessageAlreadyCreated);
            }
        }

        /// <summary>
        /// Verifies that calling CreateOperationResponseMessage if valid.
        /// </summary>
        /// <param name="synchronousCall">true if the call is to be synchronous; false otherwise.</param>
        private void VerifyCanCreateOperationResponseMessage(bool synchronousCall)
        {
            this.VerifyReaderReady();
            this.VerifyCallAllowed(synchronousCall);

            if (!this.inputContext.ReadingResponse)
            {
                this.ThrowODataException(Strings.ODataBatchReader_CannotCreateResponseOperationWhenReadingRequest);
            }

            if (this.State != ODataBatchReaderState.Operation)
            {
                this.ThrowODataException(Strings.ODataBatchReader_InvalidStateForCreateOperationResponseMessage(this.State));
            }

            if (this.operationState != OperationState.None)
            {
                this.ThrowODataException(Strings.ODataBatchReader_OperationResponseMessageAlreadyCreated);
            }
        }

        /// <summary>
        /// Verifies that calling Read is valid.
        /// </summary>
        /// <param name="synchronousCall">true if the call is to be synchronous; false otherwise.</param>
        private void VerifyCanRead(bool synchronousCall)
        {
            this.VerifyReaderReady();
            this.VerifyCallAllowed(synchronousCall);

            if (this.State == ODataBatchReaderState.Exception || this.State == ODataBatchReaderState.Completed)
            {
                throw new ODataException(Strings.ODataBatchReader_ReadOrReadAsyncCalledInInvalidState(this.State));
            }
        }

        /// <summary>
        /// Validates that the batch reader is ready to process a new read or create message request.
        /// </summary>
        private void VerifyReaderReady()
        {
            this.inputContext.VerifyNotDisposed();

            // If the operation stream was requested but not yet disposed, the batch reader can't be used to do anything.
            if (this.operationState == OperationState.StreamRequested)
            {
                throw new ODataException(Strings.ODataBatchReader_CannotUseReaderWhileOperationStreamActive);
            }
        }

        /// <summary>
        /// Verifies that a call is allowed to the reader.
        /// </summary>
        /// <param name="synchronousCall">true if the call is to be synchronous; false otherwise.</param>
        private void VerifyCallAllowed(bool synchronousCall)
        {
            if (synchronousCall)
            {
                if (!this.synchronous)
                {
                    throw new ODataException(Strings.ODataBatchReader_SyncCallOnAsyncReader);
                }
            }
            else
            {
#if ODATALIB_ASYNC
                if (this.synchronous)
                {
                    throw new ODataException(Strings.ODataBatchReader_AsyncCallOnSyncReader);
                }
#else
                Debug.Assert(false, "Async calls are not allowed in this build.");
#endif
            }
        }

        /// <summary>
        /// Increases the size of the current batch message; throws if the allowed limit is exceeded.
        /// </summary>
        internal void IncreaseBatchSize()
        {
            this.currentBatchSize++;

            if (this.currentBatchSize > this.inputContext.MessageReaderSettings.MessageQuotas.MaxPartsPerBatch)
            {
                throw new ODataException(Strings.ODataBatchReader_MaxBatchSizeExceeded(this.inputContext.MessageReaderSettings.MessageQuotas.MaxPartsPerBatch));
            }
        }

        /// <summary>
        /// Increases the size of the current change set; throws if the allowed limit is exceeded.
        /// </summary>
        internal void IncreaseChangeSetSize()
        {
            this.currentChangeSetSize++;

            if (this.currentChangeSetSize > this.inputContext.MessageReaderSettings.MessageQuotas.MaxOperationsPerChangeset)
            {
                throw new ODataException(Strings.ODataBatchReader_MaxChangeSetSizeExceeded(this.inputContext.MessageReaderSettings.MessageQuotas.MaxOperationsPerChangeset));
            }
        }

        /// <summary>
        /// Resets the size of the current change set to 0.
        /// </summary>
        internal void ResetChangeSetSize()
        {
            this.currentChangeSetSize = 0;
        }

        /// <summary>
        /// Sets the 'Exception' state and then throws an ODataException with the specified error message.
        /// </summary>
        /// <param name="errorMessage">The error message for the exception.</param>
        private void ThrowODataException(string errorMessage)
        {
            this.State = ODataBatchReaderState.Exception;
            throw new ODataException(errorMessage);
        }

        /// <summary>
        /// Catch any exception thrown by the action passed in; in the exception case move the writer into
        /// state Exception and then rethrow the exception.
        /// </summary>
        /// <typeparam name="T">The type of the result returned from the <paramref name="action"/>.</typeparam>
        /// <param name="action">The action to execute.</param>
        /// <returns>The result of the <paramref name="action"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("DataWeb.Usage", "AC0014", Justification = "Throws every time")]
        private T InterceptException<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception e)
            {
                if (ExceptionUtils.IsCatchableExceptionType(e))
                {
                    this.State = ODataBatchReaderState.Exception;
                }

                throw;
            }
        }
    }
}
