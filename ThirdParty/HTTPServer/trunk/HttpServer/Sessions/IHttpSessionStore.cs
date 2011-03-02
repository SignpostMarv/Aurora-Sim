using System;

namespace HttpServer.Sessions
{
    /// <summary>
    /// A session store is used to store and load sessions on a media.
    /// The default implementation (<see cref="MemorySessionStore"/>) saves/retrieves sessions from memory.
    /// </summary>
    public interface IHttpSessionStore
    {
        /// <summary>
        /// Load a session from the store
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns>null if session is not found.</returns>
        IHttpSession this[string sessionId] { get; }

        /// <summary>
        /// Number of minutes before a session expires.
        /// </summary>
        /// <value>Default time is 20 minutes.</value>
        int ExpireTime { get; set; }

        /// <summary>
        /// Creates a new http session with a generated id.
        /// </summary>
        /// <returns>A <see cref="IHttpSession"/> object</returns>
        IHttpSession Create();

        /// <summary>
        /// Creates a new http session with a specific id
        /// </summary>
        /// <param name="id">Id used to identify the new cookie..</param>
        /// <returns>A <see cref="IHttpSession"/> object.</returns>
        /// <remarks>
        /// Id should be generated by the store implementation if it's null or <see cref="string.Empty"/>.
        /// </remarks>
        IHttpSession Create(string id);

        /// <summary>
        /// Load an existing session.
        /// </summary>
        /// <param name="sessionId">Session id (usually retrieved from a client side cookie).</param>
        /// <returns>A session if found; otherwise null.</returns>
        IHttpSession Load(string sessionId);

        /// <summary>
        /// Save an updated session to the store.
        /// </summary>
        /// <param name="session">Session id (usually retrieved from a client side cookie).</param>
        /// <exception cref="ArgumentException">If Id property have not been specified.</exception>
        void Save(IHttpSession session);

        /// <summary>
        /// We use the flyweight pattern which reuses small objects
        /// instead of creating new each time.
        /// </summary>
        /// <param name="session">Unused session that should be reused next time Create is called.</param>
        void AddUnused(IHttpSession session);

        /// <summary>
        /// Remove expired sessions
        /// </summary>
        void Cleanup();

        /// <summary>
        /// Remove a session
        /// </summary>
        /// <param name="sessionId">id of the session.</param>
        void Remove(string sessionId);
    }
}