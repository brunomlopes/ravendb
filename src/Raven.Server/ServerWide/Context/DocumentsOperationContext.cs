﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using Raven.Client.Documents.Replication.Messages;
using Raven.Server.Documents;
using Sparrow.LowMemory;
using Voron;

namespace Raven.Server.ServerWide.Context
{
    public class DocumentsOperationContext : TransactionOperationContext<DocumentsTransaction>
    {
        private readonly DocumentDatabase _documentDatabase;

        internal ChangeVectorEntry[] LastDatabaseChangeVector;
        internal Dictionary<string, long> LastReplicationEtagFrom;

        protected override void Reset(bool forceResetLongLivedAllocator = false)
        {
            base.Reset(forceResetLongLivedAllocator);
            
            // make sure that we don't remember an old value here from a previous
            // tx. This can be an issue if we resort to context stealing from 
            // other threads, so we are going the safe route and ensuring that 
            // we always create a new instance
            LastDatabaseChangeVector = null;
            LastReplicationEtagFrom = null;
        }

        public static DocumentsOperationContext ShortTermSingleUse(DocumentDatabase documentDatabase)
        {
            var shortTermSingleUse = new DocumentsOperationContext(documentDatabase, 4096, 1024, LowMemoryFlag.None);
            return shortTermSingleUse;
        }

        public DocumentsOperationContext(DocumentDatabase documentDatabase, int initialSize, int longLivedSize, LowMemoryFlag lowMemoryFlag) :
            base(initialSize, longLivedSize, lowMemoryFlag)
        {
            _documentDatabase = documentDatabase;
        }

        protected override DocumentsTransaction CreateReadTransaction()
        {
            return new DocumentsTransaction(this, _documentDatabase.DocumentsStorage.Environment.ReadTransaction(PersistentContext, Allocator), _documentDatabase.Changes);
        }

        protected override DocumentsTransaction CreateWriteTransaction()
        {
            var tx = new DocumentsTransaction(this, _documentDatabase.DocumentsStorage.Environment.WriteTransaction(PersistentContext, Allocator), _documentDatabase.Changes);

            CurrentTxMarker = (short) tx.InnerTransaction.LowLevelTransaction.Id;

            var options = _documentDatabase.DocumentsStorage.Environment.Options;

            if ((options.TransactionsMode == TransactionsMode.Lazy || options.TransactionsMode == TransactionsMode.Danger) &&
                options.NonSafeTransactionExpiration != null && options.NonSafeTransactionExpiration < DateTime.Now)
            {
                options.TransactionsMode = TransactionsMode.Safe;
            }

            tx.InnerTransaction.LowLevelTransaction.IsLazyTransaction = 
                options.TransactionsMode == TransactionsMode.Lazy;
            // IsLazyTransaction can be overriden later by a specific feature like bulk insert

            return tx;
        }

        public StorageEnvironment Environment => _documentDatabase.DocumentsStorage.Environment;

        public DocumentDatabase DocumentDatabase => _documentDatabase;

        public bool ShouldRenewTransactionsToAllowFlushing()
        {
            // if we have the same transaction id right now, there hasn't been write since we started the transaction
            // so there isn't really a major point in renewing the transaction, since we wouldn't be releasing any 
            // resources (scratch space, mostly) back to the system, let us continue with the current one.

            return Transaction?.InnerTransaction.LowLevelTransaction.Id !=
                   _documentDatabase.DocumentsStorage.Environment.CurrentReadTransactionId ;

        }

        public bool UpdateLastDatabaseChangeVector(Guid dbId, long etag)
        {
            Debug.Assert(LastDatabaseChangeVector != null);
            var length = LastDatabaseChangeVector.Length;
            for (var i = 0; i < length; i++)
            {
                if (dbId != LastDatabaseChangeVector[i].DbId)
                    continue;

                if (LastDatabaseChangeVector[i].Etag <= etag)
                    return false;
                
                LastDatabaseChangeVector[i].Etag = etag;
                return true;
            }

            Array.Resize(ref LastDatabaseChangeVector, length + 1);
            LastDatabaseChangeVector[length] = new ChangeVectorEntry
            {
                DbId = dbId,
                Etag = etag
            };
            return true;
        }
    }
}