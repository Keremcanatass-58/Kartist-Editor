using System;

namespace Kartist.Models
{
    public class Kullanici
    {
        public int Id { get; set; }
        public string AdSoyad { get; set; }
        public string Email { get; set; }
        public string Sifre { get; set; }
        public string UyelikTipi { get; set; } = "Normal";
        public int KalanKredi { get; set; } = 5;
        public DateTime? UyelikBitisTarihi { get; set; }
        public string ProfilResmi { get; set; }
    }
}