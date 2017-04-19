//---------------------------------------------------------------------
// <copyright file="ODataMultipartMixedBatchReader.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------


namespace Microsoft.OData.Core.MultipartMixed
{
    #region Namespaces
    using System;
    using System.Diagnostics;
    using System.Text;

#if ODATALIB_ASYNC
    using System.Threading.Tasks;
#endif
    #endregion Namespaces

    internal sealed class ODataMultipartMixedBatchReader: ODataBatchReader
    {

        /// <summary>The batch stream used by the batch reader to devide a batch payload into parts.</summary>
        private readonly ODataMultipartMixedBatchReaderStream batchStream;

        internal ODataRawInputContext RawInputContext
        {
            get
            {
                return this.InputContext as ODataRawInputContext;
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="inputContext">The input context to read the content from.</param>
        /// <param name="batchBoundary">The boundary string for the batch structure itself.</param>
        /// <param name="batchEncoding">The encoding to use to read from the batch stream.</param>
        /// <param name="synchronous">true if the reader is created for synchronous operation; false for asynchronous.</param>
        internal ODataMultipartMixedBatchReader(ODataInputContext inputContext, string batchBoundary, Encoding batchEncoding, bool synchronous)
            : base(inputContext, synchronous)
        {
            Debug.Assert(inputContext != null, "inputContext != null");
            Debug.Assert(this.RawInputContext != null, "this.RawInputContext != null");
            Debug.Assert(!string.IsNullOrEmpty(batchBoundary), "!string.IsNullOrEmpty(batchBoundary)");

            this.batchStream = new ODataMultipartMixedBatchReaderStream(this.RawInputContext, batchBoundary, batchEncoding);
        }

        /// <summary>
        /// Returns the cached <see cref="ODataBatchOperationRequestMessage"/> for reading the content of an operation
        /// in a batch request.
        /// </summary>
        /// <returns>The message that can be used to read the content of the batch request operation from.</returns>
        protected override ODataBatchOperationRequestMessage CreateOperationRequestMessageImplementation()
        {
            this.ReaderOperationState = OperationState.MessageCreated;

            string requestLine = this.batchStream.ReadFirstNonEmptyLine();

            string httpMethod;
            Uri requestUri;
            this.ParseRequestLine(
                requestLine,
                ValidateQueryMethodNotAllowedInChangeSet, // HTTP GET is not allowed in change set for MIME format batching.
                out httpMethod,
                out requestUri);

            // Read all headers and create the request message
            ODataBatchOperationHeaders headers = this.batchStream.ReadHeaders();

            if (this.batchStream.ChangeSetBoundary != null)
            {
                if (this.AllowLegacyContentIdBehavior)
                {
                    // Add a potential Content-ID header to the URL resolver so that it will be available
                    // to subsequent operations.
                    string contentId;
                    if (this.ContentIdToAddOnNextRead == null && headers.TryGetValue(ODataConstants.ContentIdHeader, out contentId))
                    {
                        if (contentId != null && this.urlResolver.ContainsContentId(contentId))
                        {
                            throw new ODataException(Strings.ODataBatchReader_DuplicateContentIDsNotAllowed(contentId));
                        }

                        this.ContentIdToAddOnNextRead = contentId;
                    }
                }

                if (this.ContentIdToAddOnNextRead == null)
                {
                    throw new ODataException(Strings.ODataBatchOperationHeaderDictionary_KeyNotFound(ODataConstants.ContentIdHeader));
                }
            }

            ODataBatchOperationRequestMessage requestMessage = ODataBatchOperationRequestMessage.CreateReadMessage(
                this.batchStream,
                httpMethod,
                requestUri,
                headers,
                /*operationListener*/ this,
                this.ContentIdToAddOnNextRead,
                this.urlResolver);

            return requestMessage;
        }

        /// <summary>
        /// Returns the cached <see cref="ODataBatchOperationRequestMessage"/> for reading the content of an operation
        /// in a batch request.
        /// </summary>
        /// <returns>The message that can be used to read the content of the batch request operation from.</returns>
        protected override ODataBatchOperationResponseMessage CreateOperationResponseMessageImplementation()
        {
            this.ReaderOperationState = OperationState.MessageCreated;

            string responseLine = this.batchStream.ReadFirstNonEmptyLine();

            int statusCode = this.ParseResponseLine(responseLine);

            // Read all headers and create the response message
            ODataBatchOperationHeaders headers = this.batchStream.ReadHeaders();

            if (this.batchStream.ChangeSetBoundary != null)
            {
                if (this.AllowLegacyContentIdBehavior)
                {
                    // Add a potential Content-ID header to the URL resolver so that it will be available
                    // to subsequent operations.
                    string contentId;
                    if (this.ContentIdToAddOnNextRead == null && headers.TryGetValue(ODataConstants.ContentIdHeader, out contentId))
                    {
                        if (contentId != null && this.urlResolver.ContainsContentId(contentId))
                        {
                            throw new ODataException(Strings.ODataBatchReader_DuplicateContentIDsNotAllowed(contentId));
                        }

                        this.ContentIdToAddOnNextRead = contentId;
                    }
                }
            }

            // In responses we don't need to use our batch URL resolver, since there are no cross referencing URLs
            // so use the URL resolver from the batch message instead.
            ODataBatchOperationResponseMessage responseMessage = ODataBatchOperationResponseMessage.CreateReadMessage(
                this.batchStream,
                statusCode,
                headers,
                this.ContentIdToAddOnNextRead,
                /*operationListener*/ this,
                this.urlResolver.BatchMessageUrlResolver);

            //// NOTE: Content-IDs for cross referencing are only supported in request messages; in responses
            ////       we allow a Content-ID header but don't process it (i.e., don't add the content ID to the URL resolver).

            return responseMessage;
        }

        private void ValidateQueryMethodNotAllowedInChangeSet(string httpMethod)
        {
            // Validate the HTTP method when reading a request
            if (this.batchStream.ChangeSetBoundary != null)
            {
                // allow all methods except for GET
                if (HttpUtils.IsQueryMethod(httpMethod))
                {
                    throw new ODataException(Strings.ODataBatch_InvalidHttpMethodForChangeSetRequest(httpMethod));
                }
            }
        }

        /// <summary>
        /// Continues reading from the batch message payload.
        /// </summary>
        /// <returns>true if more items were read; otherwise false.</returns>
        protected override bool ReadImplementation()
        {
            Debug.Assert(this.ReaderOperationState != OperationState.StreamRequested, "Should have verified that no operation stream is still active.");

            switch (this.State)
            {
                case ODataBatchReaderState.Initial:
                    // The stream should be positioned at the beginning of the batch content,
                    // that is before the first boundary (or the preamble if there is one).
                    this.State = this.SkipToNextPartAndReadHeaders();
                    break;

                case ODataBatchReaderState.Operation:
                    // When reaching this state we already read the MIME headers of the operation.
                    // Clients MUST call CreateOperationRequestMessage
                    // or CreateOperationResponseMessage to read at least the headers of the operation.
                    // This is important since we need to read the ContentId header (if present) and
                    // add it to the URL resolver.
                    if (this.ReaderOperationState == OperationState.None)
                    {
                        // No message was created; fail
                        throw new ODataException(Strings.ODataBatchReader_NoMessageWasCreatedForOperation);
                    }

                    // Reset the operation state; the operation state only
                    // tracks the state of a batch operation while in state Operation.
                    this.ReaderOperationState = OperationState.None;

                    // Also add a pending ContentId header to the URL resolver now. We ensured above
                    // that a message has been created for this operation and thus the headers (incl.
                    // a potential content ID header) have been read.
                    if (this.ContentIdToAddOnNextRead != null)
                    {
                        this.urlResolver.AddContentId(this.ContentIdToAddOnNextRead);
                        this.ContentIdToAddOnNextRead = null;
                    }

                    // When we are done with an operation, we have to skip ahead to the next part
                    // when Read is called again. Note that if the operation stream was never requested
                    // and the content of the operation has not been read, we'll skip it here.
                    Debug.Assert(this.ReaderOperationState == OperationState.None, "Operation state must be 'None' at the end of the operation.");
                    this.State = this.SkipToNextPartAndReadHeaders();
                    break;

                case ODataBatchReaderState.ChangesetStart:
                    // When at the start of a changeset, skip ahead to the first operation in the
                    // changeset (or the end boundary of the changeset).
                    Debug.Assert(this.batchStream.ChangeSetBoundary != null, "Changeset boundary must have been set by now.");
                    this.State = this.SkipToNextPartAndReadHeaders();
                    break;

                case ODataBatchReaderState.ChangesetEnd:
                    // When at the end of a changeset, reset the changeset boundary and the
                    // changeset size and then skip to the next part.
                    this.ResetChangeSetSize();
                    this.batchStream.ResetChangeSetBoundary();
                    this.State = this.SkipToNextPartAndReadHeaders();
                    break;

                case ODataBatchReaderState.Exception:    // fall through
                case ODataBatchReaderState.Completed:
                    Debug.Assert(false, "Should have checked in VerifyCanRead that we are not in one of these states.");
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_ReadImplementation));

                default:
                    Debug.Assert(false, "Unsupported reader state " + this.State + " detected.");
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_ReadImplementation));
            }

            return this.State != ODataBatchReaderState.Completed && this.State != ODataBatchReaderState.Exception;
        }

        /// <summary>
        /// Skips all data in the stream until the next part is detected; then reads the part's request/response line and headers.
        /// </summary>
        /// <returns>The next state of the batch reader after skipping to the next part and reading the part's beginning.</returns>
        private ODataBatchReaderState SkipToNextPartAndReadHeaders()
        {
            bool isEndBoundary, isParentBoundary;
            bool foundBoundary = this.batchStream.SkipToBoundary(out isEndBoundary, out isParentBoundary);

            if (!foundBoundary)
            {
                // We did not find the expected boundary at all in the payload;
                // we are done reading. Depending on where we are report changeset end or completed state
                if (this.batchStream.ChangeSetBoundary == null)
                {
                    return ODataBatchReaderState.Completed;
                }
                else
                {
                    return ODataBatchReaderState.ChangesetEnd;
                }
            }

            ODataBatchReaderState nextState;
            if (isEndBoundary || isParentBoundary)
            {
                // We detected an end boundary or detected that the end boundary is missing
                // because we found a parent boundary
                nextState = this.GetEndBoundaryState();

                if (nextState == ODataBatchReaderState.ChangesetEnd)
                {
                    // Reset the URL resolver at the end of a changeset; Content IDs are
                    // unique within a given changeset.
                    this.urlResolver.Reset();
                }
            }
            else
            {
                bool currentlyInChangeSet = this.batchStream.ChangeSetBoundary != null;
                string contentId;

                // If we did not find an end boundary, we found another part
                bool isChangeSetPart = this.batchStream.ProcessPartHeader(out contentId);

                // Compute the next reader state
                if (currentlyInChangeSet)
                {
                    Debug.Assert(!isChangeSetPart, "Should have validated that nested changesets are not allowed.");
                    nextState = ODataBatchReaderState.Operation;
                    this.IncreaseChangeSetSize();
                }
                else
                {
                    // We are at the top level (not inside a changeset)
                    nextState = isChangeSetPart
                        ? ODataBatchReaderState.ChangesetStart
                        : ODataBatchReaderState.Operation;
                    this.IncreaseBatchSize();
                }

                if (!isChangeSetPart)
                {
                    // Add a potential Content-ID header to the URL resolver so that it will be available
                    // to subsequent operations.
                    Debug.Assert(this.ContentIdToAddOnNextRead == null, "Must not have a content ID to be added to a part.");

                    if (contentId != null && this.urlResolver.ContainsContentId(contentId))
                    {
                        throw new ODataException(Strings.ODataBatchReader_DuplicateContentIDsNotAllowed(contentId));
                    }

                    this.ContentIdToAddOnNextRead = contentId;
                }
            }

            return nextState;
        }


        /// <summary>
        /// Returns the next state of the batch reader after an end boundary has been found.
        /// </summary>
        /// <returns>The next state of the batch reader.</returns>
        private ODataBatchReaderState GetEndBoundaryState()
        {
            switch (this.State)
            {
                case ODataBatchReaderState.Initial:
                    // If we find an end boundary when in state 'Initial' it means that we
                    // have an empty batch. The next state will be 'Completed'.
                    return ODataBatchReaderState.Completed;

                case ODataBatchReaderState.Operation:
                    // If we find an end boundary in state 'Operation' we have finished
                    // processing an operation and found the end boundary of either the
                    // current changeset or the batch.
                    return this.batchStream.ChangeSetBoundary == null
                        ? ODataBatchReaderState.Completed
                        : ODataBatchReaderState.ChangesetEnd;

                case ODataBatchReaderState.ChangesetStart:
                    // If we find an end boundary when in state 'ChangeSetStart' it means that
                    // we have an empty changeset. The next state will be 'ChangeSetEnd'
                    return ODataBatchReaderState.ChangesetEnd;

                case ODataBatchReaderState.ChangesetEnd:
                    // If we are at the end of a changeset and find an end boundary
                    // we reached the end of the batch
                    return ODataBatchReaderState.Completed;

                case ODataBatchReaderState.Completed:
                    // We should never get here when in Completed state.
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_GetEndBoundary_Completed));

                case ODataBatchReaderState.Exception:
                    // We should never get here when in Exception state.
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_GetEndBoundary_Exception));

                default:
                    // Invalid enum value
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_GetEndBoundary_UnknownValue));
            }
        }

    }
}
