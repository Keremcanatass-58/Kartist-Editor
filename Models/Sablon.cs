using System.ComponentModel.DataAnnotations.Schema;

namespace Kartist.Models
{
    public class Sablon
    {
        public int Id { get; set; }
        public string Baslik { get; set; }
        public string ResimUrl { get; set; }
        public string Kategori { get; set; }
        public decimal Fiyat { get; set; }
        public int? SaticiId { get; set; }
        public string OnayDurumu { get; set; }
        public string JsonVerisi { get; set; }
        [NotMapped]
        public bool IsFavori { get; set; }
    }
}