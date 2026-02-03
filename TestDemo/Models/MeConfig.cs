using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestDemo.Models
{
    public class MeConfig
    {
        [Column("config_code")]
        public string ConfigCode { get; set; }

        [Column("config_promt")]
        public string ConfigPromt { get; set; }

        [Column("config_param")]
        public string ConfigParam { get; set; }

        [Column("category")]
        public string Category { get; set; }

        [Column("module")]
        public string Module { get; set; }
    }
}
