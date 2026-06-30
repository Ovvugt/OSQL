using OSQL.Core;
using OSQL.Core.Ast;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class ParserTests
{
    // ---- CREATE TABLE ----

    [Test]
    public void Parse_CreateTable_CapturesNameAndColumns()
    {
        var statement = Sql.Parse("CREATE TABLE users (id INTEGER, name TEXT)");

        Assert.That(statement, Is.TypeOf<CreateTableStatement>());
        var create = (CreateTableStatement)statement;
        Assert.That(create.TableName, Is.EqualTo("users"));
        Assert.That(create.Columns, Is.EqualTo(new[]
        {
            new ColumnDefinition("id", DataType.Integer),
            new ColumnDefinition("name", DataType.Text),
        }));
    }

    [Test]
    public void Parse_CreateTable_UnknownType_Throws()
    {
        Assert.That(() => Sql.Parse("CREATE TABLE t (id FLOAT)"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Parse_CreateTable_NotNull_SetsTheFlag()
    {
        var create = (CreateTableStatement)Sql.Parse("CREATE TABLE t (id INTEGER NOT NULL, name TEXT)");

        Assert.That(create.Columns[0].NotNull, Is.True);
        Assert.That(create.Columns[1].NotNull, Is.False);
    }

    [Test]
    public void Parse_CreateTable_ExplicitNull_IsNullable()
    {
        var create = (CreateTableStatement)Sql.Parse("CREATE TABLE t (id INTEGER NULL)");

        Assert.That(create.Columns[0].NotNull, Is.False);
    }

    [Test]
    public void Parse_CreateTable_NotWithoutNull_Throws()
    {
        Assert.That(() => Sql.Parse("CREATE TABLE t (id INTEGER NOT)"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Parse_Insert_NullLiteral_ProducesNullExpression()
    {
        var insert = (InsertStatement)Sql.Parse("INSERT INTO t VALUES (NULL, 'x')");

        Assert.That(insert.Values[0], Is.TypeOf<NullLiteralExpression>());
        Assert.That(insert.Values[1], Is.EqualTo(new LiteralExpression("x", DataType.Text)));
    }

    // ---- INSERT ----

    [Test]
    public void Parse_Insert_WithoutColumnList_LeavesColumnsNull()
    {
        var insert = (InsertStatement)Sql.Parse("INSERT INTO users VALUES (1, 'ada')");

        Assert.That(insert.TableName, Is.EqualTo("users"));
        Assert.That(insert.Columns, Is.Null);
        Assert.That(insert.Values, Has.Count.EqualTo(2));
        Assert.That(insert.Values[0], Is.EqualTo(new LiteralExpression(1L, DataType.Integer)));
        Assert.That(insert.Values[1], Is.EqualTo(new LiteralExpression("ada", DataType.Text)));
    }

    [Test]
    public void Parse_Insert_WithColumnList_CapturesColumns()
    {
        var insert = (InsertStatement)Sql.Parse("INSERT INTO users (id, name) VALUES (1, 'ada')");

        Assert.That(insert.Columns, Is.EqualTo(new[] { "id", "name" }));
    }

    // ---- SELECT ----

    [Test]
    public void Parse_SelectStar_SetsIsStarAndNoWhere()
    {
        var select = (SelectStatement)Sql.Parse("SELECT * FROM users");

        Assert.That(select.IsStar, Is.True);
        Assert.That(select.Columns, Is.Empty);
        Assert.That(select.TableName, Is.EqualTo("users"));
        Assert.That(select.Where, Is.Null);
    }

    [Test]
    public void Parse_SelectColumns_WithWhere_BuildsBinaryPredicate()
    {
        var select = (SelectStatement)Sql.Parse("SELECT id, name FROM users WHERE age >= 18");

        Assert.That(select.IsStar, Is.False);
        Assert.That(select.Columns, Is.EqualTo(new[] { "id", "name" }));

        var where = (BinaryExpression)select.Where!;
        Assert.That(where.Left, Is.EqualTo(new ColumnExpression("age")));
        Assert.That(where.Operator, Is.EqualTo(ComparisonOperator.GreaterEqual));
        Assert.That(where.Right, Is.EqualTo(new LiteralExpression(18L, DataType.Integer)));
    }

    [TestCase("=", ComparisonOperator.Equal)]
    [TestCase("<>", ComparisonOperator.NotEqual)]
    [TestCase("!=", ComparisonOperator.NotEqual)]
    [TestCase("<", ComparisonOperator.Less)]
    [TestCase("<=", ComparisonOperator.LessEqual)]
    [TestCase(">", ComparisonOperator.Greater)]
    [TestCase(">=", ComparisonOperator.GreaterEqual)]
    public void Parse_Where_MapsEveryComparisonOperator(string op, ComparisonOperator expected)
    {
        var select = (SelectStatement)Sql.Parse($"SELECT * FROM t WHERE a {op} 1");

        Assert.That(((BinaryExpression)select.Where!).Operator, Is.EqualTo(expected));
    }

    // ---- CREATE INDEX ----

    [Test]
    public void Parse_CreateIndex_Named_CapturesAllParts()
    {
        var index = (CreateIndexStatement)Sql.Parse("CREATE INDEX idx_age ON users (age)");

        Assert.That(index.IndexName, Is.EqualTo("idx_age"));
        Assert.That(index.TableName, Is.EqualTo("users"));
        Assert.That(index.ColumnName, Is.EqualTo("age"));
    }

    [Test]
    public void Parse_CreateIndex_Unnamed_LeavesNameNull()
    {
        var index = (CreateIndexStatement)Sql.Parse("CREATE INDEX ON users (age)");

        Assert.That(index.IndexName, Is.Null);
        Assert.That(index.ColumnName, Is.EqualTo("age"));
    }

    // ---- General ----

    [Test]
    public void Parse_TrailingSemicolon_IsAllowed()
    {
        Assert.That(() => Sql.Parse("SELECT * FROM users;"), Throws.Nothing);
    }

    [Test]
    public void Parse_TrailingGarbageAfterStatement_Throws()
    {
        Assert.That(() => Sql.Parse("SELECT * FROM users EXTRA"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Parse_EmptyInput_Throws()
    {
        Assert.That(() => Sql.Parse(""), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Parse_IncompleteSelect_Throws()
    {
        Assert.That(() => Sql.Parse("SELECT * FROM"), Throws.TypeOf<SqlSyntaxException>());
    }

    [Test]
    public void Parse_DoubleQuotedTableName_IsAccepted()
    {
        var select = (SelectStatement)Sql.Parse("SELECT * FROM \"test\"");

        Assert.That(select.TableName, Is.EqualTo("test"));
    }

    [Test]
    public void Parse_DoubleQuotedKeyword_IsUsableAsAName()
    {
        var select = (SelectStatement)Sql.Parse("SELECT * FROM \"select\"");

        Assert.That(select.TableName, Is.EqualTo("select"));
    }

    [Test]
    public void Parse_SingleQuotedTableName_IsRejectedAsAString()
    {
        // 'test' is a string value, never a name — the error should say so.
        var ex = Assert.Throws<SqlSyntaxException>(() => Sql.Parse("SELECT * FROM 'test'"));

        Assert.That(ex!.Message, Does.Contain("string 'test'"));
    }
}
