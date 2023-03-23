# netQL

NetQL is QueryBuilder for .Net Developer.
netQL written with C# language programming.

## Support Database
<div style="display:flex">
  <img src="https://www.itworks.id/wp-content/uploads/2021/02/oracle-1.png" height="100" alt="oracle database">
  <img src="https://labs.mysql.com/common/logos/mysql-logo.svg?v2" height="100" alt="oracle database">
  <img src="https://kinsta.com/wp-content/uploads/2022/02/postgres-logo.png" height="100" alt="oracle database">
  <img src="https://surabaya.proxsisgroup.com/wp-content/uploads/2018/01/Microsoft-SQL-Server.png" height="100" alt="oracle database">
</div>


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
var hotels = db.Select("table_hotel").ReadAsList<Hotel>();
```
### Select Once
``` C#
Hotel data = db.Select("table_hotel")
                .Where("ID", 1)
                .ReadAs<Hotel>();
```
With where condition
``` C#
var hotelInLondon = db.Select("table_hotel")
                    .Where("City", "London")
                    .OrWhere("Room", 5)
                    .ReadAsList<Hotel>();
```
Select specific columns
``` C#
var hotelCity = db.Select("name,city", "table_hotel").ReadAsList<Hotel>();
```
Select with Join
``` C#
var bookingId = "BK0001";
var booking = db.Select(
                "book.book_date, user.user_name, hotel.room_number",
                "table_order book")
              .Join("table_user user", "book.user_id", "user.id")
              .Join("table_hotel hotel", "book.hotel_id", "hotel.id")
              .Where("book.id", bookingId)
              .ReadAs<Book>();
```
Select with Join Subquery
``` C#
var countryId = "IDN";
var bookingId = "BK0001";
var bookingInLocal = db.Select(
                          "book.*, city.name, hotel.name hotel_name",
                          "table_order book")
                        .Join(subQuery => {
                          return subQuery.Select("table_city")
                                          .Where("country", countryId)
                                          .Alias("city");
                        }, "book.city_id", "city.id")
                        .Join("table_hotel hotel", "book.hotel_id", "hotel.id")
                        .Where("book.id", bookingId)
                        .ReadAs<Booking>();
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
### Delete
``` C#
var rowDeleted = db.Delete("table_hotel").Where("ID", 2).Execute();
```
### Check Existing Data
``` C#
var isLondonExist = db.Select("table_hotel")
                .Where("City", "London")
                .IsExist();
```
### Where
``` C#
  ...
  .Where("ID", 2) // Where(columnName, anyValue)
  .Where("Room", ">", 2) // Where(columnName, Condition, anyValue)
  .Where("NewPassword", "MyPassword", x => "MD5(" + x + ")") // Where(columnName, anyValue, customRaw(value))
  .Where("CheckInDate", ">", DateTime.Now, x => "MD5(" + x + ")") // Where(columnName, anyValue, customRaw(value))
  .Where("ID", 2) // Where(columnName, anyValue)
  .Where("ID", 2) // Where(columnName, anyValue)


  .OrWhere("Room", 2) // OrWhere(columnName, anyValue)
```

Thank you. Support me if you interest 😉👍
