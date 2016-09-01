﻿/*
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

namespace Apache.Ignite.Core.Impl.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Apache.Ignite.Core.Binary;
    using Apache.Ignite.Core.Impl.Binary;
    using BinaryWriter = Apache.Ignite.Core.Impl.Binary.BinaryWriter;

    /// <summary>
    /// Binarizable key-value collection with dirty item tracking.
    /// </summary>
    public class KeyValueDirtyTrackedCollection : IBinaryWriteAware
    {
        /** */
        private readonly Dictionary<string, int> _dict = new Dictionary<string, int>();

        /** */
        private readonly List<Entry> _list = new List<Entry>();

        /** Indicates where this is a new collection, not a deserialized old one. */
        private readonly bool _isNew;

        /** Removed keys. */
        private List<string> _removedKeys;

        /** */
        private bool _dirtyAll;

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyValueDirtyTrackedCollection"/> class.
        /// </summary>
        /// <param name="binaryReader">The binary reader.</param>
        internal KeyValueDirtyTrackedCollection(IBinaryRawReader binaryReader)
        {
            Debug.Assert(binaryReader != null);

            var count = binaryReader.ReadInt();

            for (var i = 0; i < count; i++)
            {
                var key = binaryReader.ReadString();

                var entry = new Entry(key, true)
                {
                    Value = binaryReader.ReadObject<object>()
                };

                _dict[key] = _list.Count;

                _list.Add(entry);
            }

            _isNew = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyValueDirtyTrackedCollection"/> class.
        /// </summary>
        public KeyValueDirtyTrackedCollection()
        {
            _isNew = true;
        }

        /// <summary>
        /// Gets the keys.
        /// </summary>
        public IEnumerable<string> GetKeys()
        {
            return _list.Select(x => x.Key);
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="T:System.Collections.ICollection" />.
        /// </summary>
        public int Count
        {
            get { return _dict.Count; }
        }

        /// <summary>
        /// Gets or sets the value with the specified key.
        /// </summary>
        public object this[string key]
        {
            get
            {
                var entry = GetEntry(key);

                if (entry == null)
                    return null;

                SetDirtyOnRead(entry);

                return entry.Value;
            }
            set
            {
                var entry = GetEntry(key);

                if (entry == null)
                {
                    entry = new Entry(key, false);

                    _dict[key] = _list.Count;
                    _list.Add(entry);

                    RemoveRemovedKey(key);
                }

                entry.IsDirty = true;

                entry.Value = value;
            }
        }

        /// <summary>
        /// Gets or sets the value at the specified index.
        /// </summary>
        public object this[int index]
        {
            get
            {
                var entry = _list[index];

                SetDirtyOnRead(entry);

                return entry.Value;
            }
            set
            {
                var entry = _list[index];

                entry.IsDirty = true;

                entry.Value = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is dirty.
        /// </summary>
        public bool IsDirty
        {
            get { return _dirtyAll || _list.Any(x => x.IsDirty); }
            set { _dirtyAll = value; }
        }

        /// <summary>
        /// Writes this object to the given writer.
        /// </summary>
        /// <param name="writer">Writer.</param>
        public void WriteBinary(IBinaryWriter writer)
        {
            var wr = (BinaryWriter) writer;

            if (_isNew || _dirtyAll || _list.Count == 0 || _list.All(x => x.IsDirty))
            {
                // Write in full mode.
                wr.WriteBoolean(true);
                wr.WriteInt(_list.Count);

                foreach (var entry in _list)
                {
                    wr.WriteString(entry.Key);

                    // ReSharper disable once AccessToForEachVariableInClosure
                    wr.WithDetach(w => w.WriteObject(entry.Value));
                }
            }
            else
            {
                // Write in diff mode.
                wr.WriteBoolean(false);

                var stream = wr.Stream;

                var countPos = stream.Position;
                var count = 0;

                wr.WriteInt(count);  // reserve count

                // Write dirty items.
                foreach (var entry in _list)
                {
                    if (!entry.IsDirty)
                        continue;

                    wr.WriteString(entry.Key);

                    // ReSharper disable once AccessToForEachVariableInClosure
                    wr.WithDetach(w => w.WriteObject(entry.Value));

                    count++;
                }

                // Write dirty item count.
                var pos = stream.Position;

                stream.Seek(countPos, SeekOrigin.Begin);
                stream.WriteInt(count);
                stream.Seek(pos, SeekOrigin.Begin);

                // Write removed keys.
                if (_removedKeys != null)
                {
                    wr.WriteInt(_removedKeys.Count);

                    foreach (var removedKey in _removedKeys)
                        wr.WriteString(removedKey);
                }
                else
                {
                    wr.WriteInt(0);
                }
            }
        }

        /// <summary>
        /// Removes the specified key.
        /// </summary>
        public void Remove(string key)
        {
            var index = GetIndex(key);

            if (index < 0)
                return;

            var entry = _list[index];

            _dict.Remove(key);
            _list.RemoveAt(index);

            if (entry.IsInitial)
                AddRemovedKey(key);
        }

        /// <summary>
        /// Removes at specified index.
        /// </summary>
        public void RemoveAt(int index)
        {
            var entry = _list[index];

            _list.RemoveAt(index);
            _dict.Remove(entry.Key);

            if (entry.IsInitial)
                AddRemovedKey(entry.Key);
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear()
        {
            foreach (var entry in _list)
            {
                if (entry.IsInitial)
                    AddRemovedKey(entry.Key);
            }

            _list.Clear();
            _dict.Clear();

            _dirtyAll = true;
        }

        /// <summary>
        /// Adds the removed key.
        /// </summary>
        private void AddRemovedKey(string key)
        {
            Debug.Assert(!_isNew);

            if (_removedKeys == null)
                _removedKeys = new List<string>();

            _removedKeys.Add(key);
        }

        /// <summary>
        /// Removes the removed key.
        /// </summary>
        private void RemoveRemovedKey(string key)
        {
            Debug.Assert(!_isNew);

            if (_removedKeys == null)
                return;

            _removedKeys.Remove(key);
        }

        /// <summary>
        /// Gets the entry.
        /// </summary>
        private Entry GetEntry(string key)
        {
            int index;

            return !_dict.TryGetValue(key, out index) ? null : _list[index];
        }

        /// <summary>
        /// Gets the index.
        /// </summary>
        private int GetIndex(string key)
        {
            int index;

            return !_dict.TryGetValue(key, out index) ? -1 : index;
        }

        /// <summary>
        /// Sets the dirty on read.
        /// </summary>
        private static void SetDirtyOnRead(Entry entry)
        {
            var type = entry.Value.GetType();

            if (IsImmutable(type))
                return;

            entry.IsDirty = true;
        }

        /// <summary>
        /// Determines whether the specified type is immutable.
        /// </summary>
        private static bool IsImmutable(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;  // Unwrap nullable.

            if (type.IsPrimitive)
                return true;

            if (type == typeof(string) || type == typeof(DateTime) || type == typeof(Guid) || type == typeof(decimal))
                return true;

            return false;
        }

        /// <summary>
        /// Inner entry.
        /// </summary>
        private class Entry
        {
            /** */
            public object Value;
            
            /** */
            public bool IsDirty;
            
            /** */
            public readonly bool IsInitial;
            
            /** */
            public readonly string Key;

            /// <summary>
            /// Initializes a new instance of the <see cref="Entry"/> class.
            /// </summary>
            public Entry(string key, bool isInitial)
            {
                Key = key;
                IsInitial = isInitial;
            }
        }
    }
}
