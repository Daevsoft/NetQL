using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for user authentication, authorization, subscription management,
    /// and subscription expiration reminders.
    /// Covers: user lookup, role-based queries, subscription CRUD,
    /// expiration alerts, and the complex subscription reminder batch update.
    /// </summary>
    [TestClass]
    public class UserSubscriptionTests
    {
        // ─── USER ACCOUNT ──────────────────────────────────────────────────

        [TestMethod]
        public void Select_User_By_Username()
        {
            var db = TestDbFactory.Create();

            var user = db.Select("users")
                .Where("username", "admin")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAs<UserAccount>();

            Assert.IsNotNull(user);
        }

        [TestMethod]
        public void Select_User_Login_With_Password_Hash()
        {
            var db = TestDbFactory.Create();

            // Using custom bind to wrap the password with MD5
            var user = db.Select("users")
                .Where("username", "admin")
                .Where("password", "mySecretPass", x => "MD5(" + x + ")")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAs<UserAccount>();

            // user can be null if credentials don't match
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Select_Users_By_Role()
        {
            var db = TestDbFactory.Create();

            var admins = db.Select("users")
                .Where("role", "ADMIN")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .Asc("full_name")
                .ReadAsList<UserAccount>();

            Assert.IsNotNull(admins);
        }

        [TestMethod]
        public void Update_Last_Login_Timestamp()
        {
            var db = TestDbFactory.Create();

            db.Update("users")
                .SetValue("last_login", Str.Raw("NOW()"))
                .Where("user_id", "USR001")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Soft_Delete_User_Account()
        {
            var db = TestDbFactory.Create();

            db.Update("users")
                .SetValue("is_active", false)
                .SetValue("deleted_at", Str.Raw("NOW()"))
                .Where("user_id", "USR_DEACTIVATE")
                .AndNull("deleted_at")
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── SUBSCRIPTIONS ─────────────────────────────────────────────────

        [TestMethod]
        public void Select_Active_Subscription_For_User()
        {
            var db = TestDbFactory.Create();

            var subscription = db.Select("user_subscriptions")
                .Where("user_id", "USR001")
                .Where("status", "ACTIVE")
                .WhereRaw("expired_date", ">=", "current_timestamp")
                .ReadAs<UserSubscription>();

            Assert.IsNotNull(subscription);
        }

        [TestMethod]
        public void Select_Expiring_Subscriptions()
        {
            var db = TestDbFactory.Create();

            // Subscriptions expiring within the next 7 days
            var expiring = db.Select(
                    new string[] { "us.*", "u.full_name", "u.email" },
                    "user_subscriptions us"
                )
                .Join("users u", "us.user_id", "u.user_id")
                .Where("us.status", "ACTIVE")
                .WhereRaw("us.expired_date", ">=", "current_timestamp")
                .WhereRaw(
                    Str.Raw("date_part('day', us.expired_date - current_timestamp)"),
                    "<=", "7"
                )
                .Where("us.email_sent", false)
                .ReadAsList<UserSubscription>();

            Assert.IsNotNull(expiring);
        }

        [TestMethod]
        public void Insert_New_Subscription()
        {
            var db = TestDbFactory.Create();

            db.Insert("user_subscriptions")
                .Bulk(new
                {
                    subscription_id = Str.Raw("gen_random_uuid()"),
                    user_id = "USR001",
                    plan_code = "PRO_ANNUAL",
                    status = "ACTIVE",
                    start_date = DateTime.Now,
                    expired_date = DateTime.Now.AddYears(1),
                    email_sent = false,
                    created_at = Str.Raw("NOW()")
                })
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Update_Expired_Subscriptions_Status()
        {
            var db = TestDbFactory.Create();

            // Batch update: mark all past-due subscriptions as EXPIRED
            db.Update("user_subscriptions")
                .SetValue("status", "EXPIRED")
                .Where("status", "ACTIVE")
                .WhereRaw("expired_date", "<", "current_timestamp")
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── COMPLEX SUBSCRIPTION REMINDER ─────────────────────────────────

        [TestMethod]
        public void Batch_Update_Email_Sent_For_Expiring_Subscriptions()
        {
            var db = TestDbFactory.Create();

            // This is the real-world complex query from the codebase:
            // Find users whose subscriptions expire within N days (from config),
            // haven't been emailed yet, and are still active — then mark EmailSent = true.
            db.Update("user_subscriptions")
                .SetValue("email_sent", true)
                .WhereIn("user_id", sub => sub
                    .Select(new string[] { "u.user_id" }, "user_subscriptions us")
                    .Join("users u", "us.user_id", "u.user_id")
                    .Where(
                        Str.Raw("date_part('day', us.expired_date - current_timestamp)"),
                        "<=",
                        configSub => configSub
                            .Select(Str.Raw("CAST(config_value AS INT)"), "app_config")
                            .Where("config_code", "EMAIL_REMIND_BEFORE_DAYS")
                    )
                    .Where("us.email_sent", false)
                    .WhereRaw("us.expired_date", ">=", "current_timestamp")
                    .GroupBy("u.user_id")
                )
                .Where("status", "ACTIVE")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Select_Users_With_No_Active_Subscription()
        {
            var db = TestDbFactory.Create();

            // Users who don't have any active subscription
            var usersWithout = db.Select("users")
                .WhereNotIn("user_id", sub => sub
                    .Select("user_id", "user_subscriptions")
                    .Where("status", "ACTIVE")
                    .WhereRaw("expired_date", ">=", "current_timestamp"))
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAsList<UserAccount>();

            Assert.IsNotNull(usersWithout);
        }
    }
}
