// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import java.nio.file.*;
import java.sql.*;
import java.util.*;

/** Embedded H2 owns event deduplication and the transactional review outbox. */
public final class Inbox implements AutoCloseable {
    private final Connection db;
    public record Row(String id, String inputHash, String eventJson, String state, String session, String query, String response, boolean recovered) {}
    public Inbox(Path directory, String identity) throws Exception {
        Files.createDirectories(directory);
        db = DriverManager.getConnection("jdbc:h2:file:" + directory.toAbsolutePath().resolve("inbox") + ";DB_CLOSE_ON_EXIT=FALSE;WRITE_DELAY=0", "sa", "");
        try (var statement = db.createStatement()) {
            statement.execute("CREATE TABLE IF NOT EXISTS identity (id INT PRIMARY KEY, config VARCHAR NOT NULL)");
            statement.execute("CREATE TABLE IF NOT EXISTS events (id VARCHAR PRIMARY KEY, input_hash VARCHAR NOT NULL, event_json CLOB NOT NULL, state VARCHAR NOT NULL, session VARCHAR, query CLOB, response CLOB, recovered BOOLEAN DEFAULT FALSE)");
            statement.execute("CREATE TABLE IF NOT EXISTS outbox (event_id VARCHAR PRIMARY KEY REFERENCES events(id), packet CLOB NOT NULL)");
        }
        try (var select = db.createStatement(); var rows = select.executeQuery("SELECT config FROM identity WHERE id=1")) {
            if (rows.next()) {
                if (!rows.getString(1).equals(identity)) { db.close(); throw new IllegalArgumentException("Inbox belongs to another identity/configuration."); }
            } else try (var insert = db.prepareStatement("INSERT INTO identity VALUES (1, ?)")) { insert.setString(1, identity); insert.executeUpdate(); }
        }
    }
    public synchronized boolean accept(OrderEvent event) throws Exception {
        String json = FilesUtil.json(event), hash = FilesUtil.hash(json);
        Row prior = get(event.eventId());
        if (prior != null) {
            if (!prior.inputHash().equals(hash)) throw new IllegalArgumentException("Event ID reused with changed input: " + event.eventId());
            return false;
        }
        try (var insert = db.prepareStatement("INSERT INTO events(id,input_hash,event_json,state) VALUES (?,?,?,'new')")) {
            insert.setString(1, event.eventId()); insert.setString(2, hash); insert.setString(3, json); insert.executeUpdate();
        }
        return true;
    }
    public synchronized Row get(String id) throws Exception {
        try (var select = db.prepareStatement("SELECT * FROM events WHERE id=?")) {
            select.setString(1, id);
            try (var r = select.executeQuery()) { return r.next() ? new Row(id, r.getString("input_hash"), r.getString("event_json"), r.getString("state"), r.getString("session"), r.getString("query"), r.getString("response"), r.getBoolean("recovered")) : null; }
        }
    }
    public synchronized List<Row> rows() throws Exception {
        var result = new ArrayList<Row>();
        try (var select = db.createStatement(); var rows = select.executeQuery("SELECT id FROM events ORDER BY id")) { while(rows.next()) result.add(get(rows.getString(1))); }
        return result;
    }
    public synchronized boolean claim(String id, String session, String query) throws Exception {
        try (var update = db.prepareStatement("UPDATE events SET state='uncertain',session=?,query=? WHERE id=? AND state='new'")) {
            update.setString(1, session); update.setString(2, query); update.setString(3, id); return update.executeUpdate() == 1;
        }
    }
    public synchronized void response(String id, String response, boolean recovered) throws Exception {
        try (var update = db.prepareStatement("UPDATE events SET state='review_required',response=?,recovered=? WHERE id=?")) {
            update.setString(1, response); update.setBoolean(2, recovered); update.setString(3, id); update.executeUpdate();
        }
    }
    public synchronized void finish(String id, String packet) throws Exception {
        db.setAutoCommit(false);
        try (var insert = db.prepareStatement("MERGE INTO outbox(event_id,packet) KEY(event_id) VALUES (?,?)"); var update = db.prepareStatement("UPDATE events SET state='complete' WHERE id=?")) {
            insert.setString(1, id); insert.setString(2, packet); insert.executeUpdate(); update.setString(1, id); update.executeUpdate(); db.commit();
        } catch (Exception e) { db.rollback(); throw e; }
        finally { db.setAutoCommit(true); }
    }
    public synchronized Map<String,String> packets() throws Exception {
        var packets = new TreeMap<String,String>();
        try (var select = db.createStatement(); var rows = select.executeQuery("SELECT event_id,packet FROM outbox ORDER BY event_id")) { while(rows.next()) packets.put(rows.getString(1), rows.getString(2)); }
        return packets;
    }
    @Override public void close() throws SQLException { db.close(); }
}
