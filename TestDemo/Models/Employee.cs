using System.ComponentModel.DataAnnotations.Schema;

namespace TestDemo.Models
{
    /// <summary>
    /// Core employee record — HR module.
    /// </summary>
    public class Employee
    {
        [Column("employee_id")]
        public string EmployeeId { get; set; }

        [Column("employee_code")]
        public string EmployeeCode { get; set; }

        [Column("full_name")]
        public string FullName { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("department_id")]
        public string DepartmentId { get; set; }

        [Column("position_id")]
        public string PositionId { get; set; }

        [Column("hire_date")]
        public DateTime? HireDate { get; set; }

        [Column("salary")]
        public decimal? Salary { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }
    }

    public class Department
    {
        [Column("department_id")]
        public string DepartmentId { get; set; }

        [Column("department_name")]
        public string DepartmentName { get; set; }

        [Column("parent_id")]
        public string ParentId { get; set; }

        [Column("manager_id")]
        public string ManagerId { get; set; }
    }
}
