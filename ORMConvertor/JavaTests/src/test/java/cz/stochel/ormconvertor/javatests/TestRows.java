package cz.stochel.ormconvertor.javatests;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;

/**
 * Rows a level-4 scenario needs before it can write its own (decisions 016 and 087). The
 * fixture schema states real foreign keys, so an order cannot be stored until a customer
 * exists - and the customer has to be written on the connection the scenario's transaction
 * already holds, or the rollback at the end would leave it behind.
 *
 * <p>Nothing here goes through a generated artifact on purpose: it is the scenario's
 * scaffolding, not its subject. What the generated artifact does is stored and read back
 * by the test itself.
 */
public final class TestRows {

    private TestRows() {
    }

    /**
     * Inserts a customer and returns the key the database generated for it. The column is
     * an IDENTITY, so the value is read back rather than chosen - which is also why the
     * row cannot simply be written with a fixed key.
     */
    public static int insertCustomer(Connection connection, String name) throws SQLException {
        String sql = "INSERT INTO [" + TestDatabase.schemaName() + "].[Customers] ([Name]) "
                + "OUTPUT INSERTED.[CustomerId] VALUES (?)";

        try (PreparedStatement statement = connection.prepareStatement(sql)) {
            statement.setString(1, name);

            try (ResultSet keys = statement.executeQuery()) {
                if (!keys.next()) {
                    throw new IllegalStateException("The customer insert returned no key.");
                }
                return keys.getInt(1);
            }
        }
    }
}
