using System.ComponentModel.DataAnnotations;

namespace appErasmus.Models
{
    /// <summary>
    /// Representa uma unidade orgânica (Escola) pertencente ao Instituto Politécnico.
    /// Serve como agregadora de vários cursos (ex: Escola Superior de Tecnologia de Tomar).
    /// </summary>
    public class Escola
    {
        /// <summary>
        /// Identificador único da escola na base de dados (Chave Primária).
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Código ou sigla oficial da instituição (ex: "ESTT", "ESGT", "ESTA").
        /// Utilizado para listagens curtas e referências rápidas.
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Código da Escola")]
        public string CodigoEscola { get; set; } = string.Empty;

        /// <summary>
        /// Designação completa e oficial da Escola (ex: "Escola Superior de Tecnologia de Tomar").
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Nome da Escola")]
        public string Nome { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação: Relação Um-para-Muitos com os Cursos.
        /// Representa a totalidade da oferta formativa lecionada nesta escola específica.
        /// </summary>
        public virtual ICollection<Curso> Cursos { get; set; } = [];
    }
}