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
# Connection
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

Create model for result :
``` C#
public class Hotel {
  public int ID { set; get; }
  public string Name { set; get; }
  public int Room { set; get; }
  public string City { set; get; }
}
``` 
Or use `Column` attribute for different name of column in database
``` C#
public class Hotel {
  [Column("room_id")]
  public int ID { set; get; }
  
  [Column("room_name")]
  public string Name { set; get; }

  [Column("no_room")]
  public int Room { set; get; }
  
  [Column("city_name")]
  public string City { set; get; }
}
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
var hotelCity = db.Select(new string[]{ "name", "city" }, "table_hotel").ReadAsList<Hotel>();
```
Select with Join
``` C#
var bookingId = "BK0001";
var booking = db.Select(
                new string[]{ "book.book_date", "user.user_name", "hotel.room_number" },
                "table_order book")
              .Join("table_user user", "book.user_id", "user.id")
              .Join("table_hotel hotel", "book.hotel_id", "hotel.id")
              .Where("book.id", bookingId)
              .ReadAs<Book>();
```
Select with raw columns
``` C#
var hotelCity = db.Select( 
                    Str.Raw("COUNT(id) as total_hotel, city_id")
                  , "table_hotel")
                .GroupBy("city_id")
                .ReadAsList<HotelCity>();
```
Or mixed other column
``` C#
var hotelCity = db.Select( new string[]{
                    Str.Raw("COUNT(id) as total_hotel"),
                    "city_id"// ,... other columns
                  }
                  , "table_hotel")
                .GroupBy("city_id")
                .ReadAsList<HotelCity>();
```
Select with Join Subquery
``` C#
var countryId = "IDN";
var bookingId = "BK0001";
var bookingInLocal = db.Select(
                          "book.*, city.name, hotel.name hotel_name",
                          "table_order book")
                        .Join(subQuery => subQuery
                                          .Select("table_city")
                                          .Where("country", countryId)
                                          .Alias("city");
                              , "book.city_id", "city.id")
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
### With complex query for update
``` C#
  db.Update("UserSubscriptions")
      .SetValue("EmailSent", true)
      .WhereIn("UserId", _db => _db
                  .Select(new string[] {
                          "b.Id"
                      }, "UserSubscriptions a")
                  .Join("Users b", "a.UserId", "b.Id")
                  .Where(Str.Raw("date_part('day', a.\"ExpiredDate\" - current_timestamp)"),
                          "<=", subQuery => subQuery
                                              .Select(Str.Raw("cast(config_param as Int)"), "app_config")
                                              .Where("config_id", "EMAIL_REMIND_SUBSCRIPTION_BEFORE_DAY")
                          )
                  .Where("a.EmailSent", false)
                  .WhereRaw("a.ExpiredDate", ">=", "current_timestamp")
                  .GroupBy("b.Id"))
      .Where("Status", UserSubscriptionsStatus.ACTIVE)
      .Execute();
```
### Where
``` C#
  ...
  .Where("ID", 2) // Where(columnName, anyValue)
  .Where("Room", ">", 2) // Where(columnName, Condition, anyValue)
  .Where("NewPassword", "MyPassword", x => "MD5(" + x + ")") // Where(columnName, anyValue, customRaw(value))
  .Where("CheckInDate", ">", DateTime.Now) // Where(columnName, anyValue, customRaw(value))
  .Where("ID", 2) // Where(columnName, anyValue)
  .Where("ID", 2) // Where(columnName, anyValue)
  .Where(Str.Raw("date_part('day', \"ExpiredDate\" - current_timestamp)"), 
          "<", subQuery => {
              return subQuery.Select("value", "configTable").Where("configCode", "REMIND_EMAIL");
          })
  .WhereIn("CityId", new int[]{ 11, 12, 13})
  .WhereIn("CityName", new string[]{ "Jakarta", "Bandung", "Bogor"})
  // Or with subquery
  .WhereIn("CityId", subQuery => subQuery.Select("CityId", "Hotels")
                                          .Where("Availability", true))
  .OrWhere("Room", 2) // OrWhere(columnName, anyValue)
```

Thank you. Support me if you interest 😉👍
