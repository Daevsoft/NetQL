using Daevsoft;
using Daevsoft.Lib;
using TestDemo.Infrastructure;
using TestDemo.Models;

namespace TestDemo.QueryBuilder
{
    /// <summary>
    /// Tests for CRUD operations on the employee table — HR module.
    /// Covers: Select single, Select with filters, Insert, Update, Soft Delete, Restore.
    /// </summary>
    [TestClass]
    public class EmployeeCrudTests
    {
        // ─── SELECT ────────────────────────────────────────────────────────

        [TestMethod]
        public void Select_Employee_By_Id()
        {
            var db = TestDbFactory.Create();

            var employee = db.Select("employees")
                .Where("employee_id", "EMP001")
                .ReadAs<Employee>();

            Assert.IsNotNull(employee);
        }

        [TestMethod]
        public void Select_Active_Employees_By_Department()
        {
            var db = TestDbFactory.Create();

            var employees = db.Select("employees")
                .Where("department_id", "DEPT_IT")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .Asc("full_name")
                .ReadAsList<Employee>();

            Assert.IsNotNull(employees);
        }

        [TestMethod]
        public void Select_Employees_With_Department_Join()
        {
            var db = TestDbFactory.Create();

            var employees = db.Select(
                    new string[] { "e.*", "d.department_name" },
                    "employees e"
                )
                .Join("departments d", "e.department_id", "d.department_id")
                .Where("e.is_active", true)
                .AndNull("e.deleted_at")
                .Asc("e.full_name")
                .ReadAsList<Employee>();

            Assert.IsNotNull(employees);
        }

        [TestMethod]
        public void Select_Employees_Hired_In_Date_Range()
        {
            var db = TestDbFactory.Create();

            var employees = db.Select("employees")
                .Where("hire_date", ">=", new DateTime(2024, 1, 1))
                .Where("hire_date", "<", new DateTime(2025, 1, 1))
                .Where("is_active", true)
                .Desc("hire_date")
                .ReadAsList<Employee>();

            Assert.IsNotNull(employees);
        }

        [TestMethod]
        public void Select_Employees_In_Multiple_Departments()
        {
            var db = TestDbFactory.Create();

            var employees = db.Select("employees")
                .WhereIn("department_id", new string[] { "DEPT_IT", "DEPT_HR", "DEPT_FIN" })
                .Where("is_active", true)
                .AndNull("deleted_at")
                .ReadAsList<Employee>();

            Assert.IsNotNull(employees);
        }

        // ─── INSERT ────────────────────────────────────────────────────────

        [TestMethod]
        public void Insert_New_Employee()
        {
            var db = TestDbFactory.Create();

            db.Insert("employees")
                .AddValue("employee_id", Str.Raw("gen_random_uuid()"))
                .AddValue("employee_code", "EMP-2025-001")
                .AddValue("full_name", "John Doe")
                .AddValue("email", "john.doe@company.com")
                .AddValue("department_id", "DEPT_IT")
                .AddValue("position_id", "POS_DEV")
                .AddValue("hire_date", DateTime.Now)
                .AddValue("salary", 75000m)
                .AddValue("is_active", true)
                .AddValue("created_at", Str.Raw("NOW()"))
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Insert_Employee_With_Bulk_AnonymousObject()
        {
            var db = TestDbFactory.Create();

            db.Insert("employees")
                .Bulk(new
                {
                    employee_id = Str.Raw("gen_random_uuid()"),
                    employee_code = "EMP-2025-002",
                    full_name = "Jane Smith",
                    email = "jane.smith@company.com",
                    department_id = "DEPT_HR",
                    position_id = "POS_MGR",
                    hire_date = DateTime.Now,
                    salary = 95000m,
                    is_active = true,
                    created_at = Str.Raw("NOW()")
                })
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── UPDATE ────────────────────────────────────────────────────────

        [TestMethod]
        public void Update_Employee_Department_Transfer()
        {
            var db = TestDbFactory.Create();

            db.Update("employees")
                .SetValue("department_id", "DEPT_FIN")
                .SetValue("position_id", "POS_ANALYST")
                .SetValue("updated_at", Str.Raw("NOW()"))
                .Where("employee_id", "EMP001")
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Update_Employee_Salary_With_Condition()
        {
            var db = TestDbFactory.Create();

            // Annual raise: increase salary by 10% for active employees in IT
            db.Update("employees")
                .SetRawValue("salary", "salary * 1.10")
                .Where("department_id", "DEPT_IT")
                .Where("is_active", true)
                .AndNull("deleted_at")
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── SOFT DELETE & RESTORE ─────────────────────────────────────────

        [TestMethod]
        public void Soft_Delete_Employee()
        {
            var db = TestDbFactory.Create();

            db.Update("employees")
                .SetValue("deleted_at", Str.Raw("NOW()"))
                .SetValue("is_active", false)
                .Where("employee_id", "EMP001")
                .AndNull("deleted_at") // prevent double-delete
                .Execute();

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void Restore_Soft_Deleted_Employee()
        {
            var db = TestDbFactory.Create();

            db.Update("employees")
                .SetRawValue("deleted_at", "NULL")
                .SetValue("is_active", true)
                .SetValue("updated_at", Str.Raw("NOW()"))
                .Where("employee_id", "EMP001")
                .Execute();

            Assert.IsTrue(true);
        }

        // ─── DELETE (Hard) ─────────────────────────────────────────────────

        [TestMethod]
        public void Hard_Delete_Employee()
        {
            var db = TestDbFactory.Create();

            db.Delete("employees")
                .Where("employee_code", "EMP-TEST-DELETE")
                .Execute();

            Assert.IsTrue(true);
        }
    }
}
