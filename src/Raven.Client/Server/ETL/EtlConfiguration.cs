﻿using System;
using System.Collections.Generic;
using Sparrow;

namespace Raven.Client.Server.ETL
{
    public class EtlConfiguration<T> : IDatabaseTask where T : EtlDestination
    {
        private long? _id;

        public T Destination { get; set; }

        public List<Transformation> Transforms { get; set; } = new List<Transformation>();

        public bool Disabled { get; set; }
        
        public virtual bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            Destination.Validate(ref errors);

            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var script in Transforms)
            {
                script.Validate(ref errors);

                if (uniqueNames.Add(script.Name) == false)
                    errors.Add($"Script name '{script.Name}' name is already defined. The script names need to be unique");
            }
            
            return errors.Count == 0;
        }

        public long Id => _id ?? (_id = (long)Hashing.XXHash64.Calculate(Destination.Name.ToLowerInvariant(), Encodings.Utf8)).Value;

        public EtlType EtlType => Destination is RavenDestination ? EtlType.Raven : EtlType.Sql;

        public ulong GetTaskKey()
        {
            return (ulong)Id;
        }
    }
}