# netQL

NetQL is QueryBuilder for .Net Developer.
netQL written with C# language programming.

## Demo
Create model for result :
``` C#
public class Hotel {
  public int ID { set; get; }
  public string Name { set; get; }
  public int Room { set; get; }
  public string City { set; get; }
}
```
Create instance of connection
MySql Connection :
``` C#
using MySql.Data.MySqlClient;
...
string connectionString = "server=localhost;user=root;database=yourdb;port=3306;password=yourpw";
MySqlConnection connection = new MySqlConnection(connectionString);

NetQL<MySqlConnection> db = new NetQL<MySqlConnection>(connection, Provider.MySql);
...

```
SQL Server Connection :
``` C#
using System.Data.SqlClient;
...
string connectionString = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;";
SqlConnection connection = new SqlConnection(connectionString);

NetQL<SqlConnection> db = new NetQL<SqlConnection>(connection, Provider.SqlServer);
```

### Select All
``` C#
var hotels = db.Select("table_hotel").ReadAsLive<Hotel>();
```
### Select Once
``` C#
Hotel data = db.Select("table_hotel")
                .Where("ID", 1)
                .ReadAs<Hotel>();
```
With where condition
``` C#
var hotelInLondon = db.Select("table_hotel").Where("city", "London").ReadAsList<Hotel>();
```
Select custom column
``` C#
var hotelCity = db.Select("name,city", "table_hotel").ReadAsList<Hotel>();
```

### Insert
``` C#
var result = db.Insert("table_hotel")
                .AddValue("Name", "Refles")
                .AddValue("Room", 129)
                .AddValue("City", "Paris")
                .Execute();
```
### Update
``` C#
var result = db.Update("table_hotel")
                .SetValue("Name", "Vave Hotel")
                .SetValue("Room", 200)
                .Where("ID", 5)
                .Execute();
```
### Check Existing Data
``` C#
var isLondonExist = db.Select("table_hotel")
                .Where("City", "London")
                .IsExist();
```

Thank you. Support me if you interest 😉👍
