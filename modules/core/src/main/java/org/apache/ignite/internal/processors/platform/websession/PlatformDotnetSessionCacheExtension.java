/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

package org.apache.ignite.internal.processors.platform.websession;

import org.apache.ignite.IgniteCheckedException;
import org.apache.ignite.internal.binary.BinaryRawReaderEx;
import org.apache.ignite.internal.processors.platform.cache.PlatformCache;
import org.apache.ignite.internal.processors.platform.cache.PlatformCacheExtension;
import org.apache.ignite.internal.processors.platform.memory.PlatformMemory;
import org.apache.ignite.internal.util.typedef.internal.S;

import java.sql.Timestamp;
import java.util.UUID;

/**
 * Custom entry processor invoker.
 */
public class PlatformDotnetSessionCacheExtension implements PlatformCacheExtension {
    /** Extension ID. */
    private static final int EXT_ID = 0;

    /** Operation: session lock. */
    private static final int OP_SESSION_LOCK = 1;

    /** Operation: session set/unlock. */
    private static final int OP_SESSION_SET_AND_UNLOCK = 2;

    /** {@inheritDoc} */
    @Override public int id() {
        return EXT_ID;
    }

    /** {@inheritDoc} */
    @SuppressWarnings("unchecked")
    @Override public long processInOutStreamLong(PlatformCache target, int type, BinaryRawReaderEx reader,
        PlatformMemory mem) throws IgniteCheckedException {
        switch (type) {
            case OP_SESSION_LOCK: {
                String key = reader.readString();
                UUID lockNodeId = reader.readUuid();
                long lockId = reader.readLong();
                Timestamp lockTime = reader.readTimestamp();

                Object res = target.rawCache().invoke(key,
                    new PlatformDotnetSessionLockProcessor(lockNodeId, lockId, lockTime));

                return target.writeResult(mem, res);
            }

            case OP_SESSION_SET_AND_UNLOCK:
                String key = reader.readString();

                PlatformDotnetSessionSetAndUnlockProcessor proc;

                if (reader.readBoolean()) {
                    PlatformDotnetSessionData data = reader.readObject();

                    proc = new PlatformDotnetSessionSetAndUnlockProcessor(data);
                }
                else {
                    UUID lockNodeId = reader.readUuid();
                    long lockId = reader.readLong();

                    proc = new PlatformDotnetSessionSetAndUnlockProcessor(lockNodeId, lockId);
                }

                target.rawCache().invoke(key, proc);

                return target.writeResult(mem, null);
        }

        throw new IgniteCheckedException("Unsupported operation type: " + type);
    }

    /** {@inheritDoc} */
    @Override public String toString() {
        return S.toString(PlatformDotnetSessionCacheExtension.class, this);
    }
}