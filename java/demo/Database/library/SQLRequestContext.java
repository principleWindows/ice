// **********************************************************************
//
// Copyright (c) 2003-2011 ZeroC, Inc. All rights reserved.
//
// This copy of Ice is licensed to you under the terms described in the
// ICE_LICENSE file included in this distribution.
//
// **********************************************************************

//
// A SQL request context encapsulates SQL resources allocated in the
// process of executing a request, such as the database connection,
// and associated SQL statements.
//
// The request context is automatically destroyed at the end of a
// request, or if obtain is called it must be destroyed manually by
// calling destroy.
//
// When the request context is destroyed, the transaction is either
// automatically committed or rolled back, depending whether the
// request executed successfully.
//
class SQLRequestContext
{
    public static SQLRequestContext
    getCurrentContext()
    {
        synchronized(_contextMap)
        {
            return _contextMap.get(Thread.currentThread());
        }
    }

    public static void
    initialize(Ice.Logger logger, ConnectionPool pool)
    {
        assert _logger == null;
        assert _pool == null;

        _logger = logger;
        _pool = pool;
    }

    public java.sql.PreparedStatement
    prepareStatement(String sql)
        throws java.sql.SQLException
    {
        java.sql.PreparedStatement stmt = _conn.prepareStatement(sql);
        _statements.add(stmt);
        return stmt;
    }

    public java.sql.PreparedStatement
    prepareStatement(String sql, int autoGeneratedKeys)
        throws java.sql.SQLException
    {
        java.sql.PreparedStatement stmt = _conn.prepareStatement(sql, autoGeneratedKeys);
        _statements.add(stmt);
        return stmt;
    }

    // Called to obtain ownership of the context. The context is no
    // longer destroyed automatically when the current request has
    // completed.
    public void
    obtain()
    {
        if(_trace)
        {
            _logger.trace("SQLRequestContext", "obtain context: " + this +
                          " thread: " + Thread.currentThread());
        }
        _obtain = true;
    }

    public void
    destroy(boolean commit)
    {
        // Must only be called on an obtained context.
        assert _obtain;
        destroyInternal(commit);
    }

    public void
    error(String prefix, Exception ex)
    {
        java.io.StringWriter sw = new java.io.StringWriter();
        java.io.PrintWriter pw = new java.io.PrintWriter(sw);
        ex.printStackTrace(pw);
        pw.flush();
        _logger.error(prefix + ": error:\n" + sw.toString());
    }

    SQLRequestContext()
    {
        _conn = _pool.acquire();

        synchronized(_contextMap)
        {
            if(_trace)
            {
                _logger.trace("SQLRequestContext", "create new context: " + this +
                              " thread: " + Thread.currentThread() +
                              ": connection: " + _conn);
            }

            _contextMap.put(Thread.currentThread(), this);
        }
    }

    // Called only during the dispatch process.
    void
    destroyFromDispatch(boolean commit)
    {
        synchronized(_contextMap)
        {
            // Remove the current context from the thread->context
            // map.
            SQLRequestContext context = _contextMap.remove(Thread.currentThread());
            assert context != null;
        }

        // If the context was obtained then don't destroy.
        if(!_obtain)
        {
            destroyInternal(commit);
        }
    }

    private void
    destroyInternal(boolean commit)
    {
        // Release all resources.
        try
        {
            if(commit)
            {
                _conn.commit();
                if(_trace)
                {
                    _logger.trace("SQLRequestContext", "commit context: " + this);
                }
            }
            else
            {
                _conn.rollback();
                if(_trace)
                {
                    _logger.trace("SQLRequestContext", "rollback context: " + this);
                }
            }

            for(java.sql.Statement p : _statements)
            {
                p.close();
            }
        }
        catch(java.sql.SQLException e)
        {
            error("SQLRequestContext", e);
        }

        _pool.release(_conn);

        _statements.clear();
        _conn = null;
    }

    // A map of threads to request contexts.
    private static java.util.Map<Thread, SQLRequestContext> _contextMap =
        new java.util.HashMap<Thread, SQLRequestContext>();

    private static Ice.Logger _logger = null;
    private static ConnectionPool _pool = null;

    private boolean _trace = true;
    private java.util.List<java.sql.Statement> _statements = new java.util.LinkedList<java.sql.Statement>();
    private java.sql.Connection _conn;
    private boolean _obtain = false;
}
