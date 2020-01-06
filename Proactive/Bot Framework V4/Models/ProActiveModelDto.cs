using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProactiveBot.Models
{
    public class PatientSymptomInfoDto
    {
        public string Key { get; set; }
        public string Identifier { get; set; }
        public string Doctor { get; set; }
        public string PatientName { get; set; }
        public string PatientDob { get; set; }
        public string Symptoms { get; set; }
        public string SignSymptomMention { get; set; }
        public string MedicationMention { get; set; }
        public string DiseaseDisorderMention { get; set; }
        public string AnatomicalSiteMention { get; set; }

    }
}
