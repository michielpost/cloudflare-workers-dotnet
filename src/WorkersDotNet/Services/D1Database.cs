using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// Thin wrapper around the D1 binding that removes the repeated
    /// read-and-materialize boilerplate from the services. Call sites build a
    /// <see cref="D1PreparedStatement"/> (binding each value individually — the
    /// compiler cannot spread a runtime array into a <c>params</c> binding) and
    /// hand it to one of the helpers below.
    /// </summary>
    public sealed class D1Database
    {
        private readonly ID1Database _db;

        public D1Database(ID1Database db)
        {
            _db = db;
        }

        /// <summary>Prepares a statement so callers can bind values and run it.</summary>
        public D1PreparedStatement Prepare(string sql)
        {
            return _db.Prepare(sql);
        }

        /// <summary>Runs a select and returns the rows (empty when none).</summary>
        public async Task<IReadOnlyList<T>> AllAsync<T>(D1PreparedStatement stmt)
        {
            var result = await stmt.AllAsync<T>();
            return result?.Results ?? new List<T>();
        }

        /// <summary>Returns the first row, or null when there is none or the query fails.</summary>
        public async Task<T?> FirstAsync<T>(D1PreparedStatement stmt) where T : class
        {
            try
            {
                return await stmt.FirstAsync<T>();
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>Runs a <c>SELECT COUNT(*) ...</c> style query and returns the single value.</summary>
        public async Task<int> CountAsync(D1PreparedStatement stmt)
        {
            var row = await stmt.FirstAsync<CountRow>();
            return row is null ? 0 : row.Value;
        }

        /// <summary>Runs a non-query statement (insert, update, delete).</summary>
        public async Task ExecuteAsync(D1PreparedStatement stmt)
        {
            await stmt.RunAsync();
        }

        /// <summary>
        /// Runs a non-query statement and reports whether it applied. Used when
        /// the statement's success (e.g. a UNIQUE constraint) must be detected.
        /// </summary>
        public async Task<bool> ExecuteSucceededAsync(D1PreparedStatement stmt)
        {
            var result = await stmt.RunAsync();
            return result is not null && result.Success;
        }
    }
}
