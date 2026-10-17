using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace appErasmus.Models {
    /// <summary>
    /// Enum para suportar a distinção entre alunos nacionais e internacionais.
    /// </summary>
    public enum EnumTipoDocumentoIdentificacao {
        [Display(Name = "Cartão de Cidadão")]
        CartaoCidadao = 1,

        [Display(Name = "Passaporte")]
        Passaporte = 2
    }

    /// <summary>
    /// Classe que representa um aluno do sistema Erasmus.
    /// Esta classe herda da classe Utilizador, o que significa que inclui todas as propriedades base da mesma
    /// </summary>
    [Index(nameof(NumeroAluno), IsUnique = true)] // Garante que o Número de Aluno é ÚNICO
    [Index(nameof(NIF), IsUnique = true)] // Garante que o NIF é ÚNICO
    [Index(nameof(NumeroDocumento), IsUnique = true)]
    public class Aluno : Utilizador {
        /// <summary>
        /// Número de aluno no sistema do IPT.
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Número de Aluno")]
        public int NumeroAluno { get; set; }

        /// <summary>
        /// Ano curricular atual do aluno.
        /// Indica em que ano do curso o estudante se encontra.
        /// Exemplo: 1 ano, 2 ano ou 3 ano.
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Ano Curricular")]
        public int AnoCurricular { get; set; }

        /// <summary>
        /// Número de Identificação Fiscal do aluno.
        /// Apesar de ser composto apenas por dígitos, é guardado como string para evitar perda de zeros à esquerda 
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [StringLength(9, MinimumLength = 9, ErrorMessage = "O {0} deve ter exatamente {1} dígitos.")]
        [RegularExpression(@"^\d{9}$", ErrorMessage = "O NIF deve conter exatamente 9 dígitos.")]
        [Display(Name = "NIF")]
        public string NIF { get; set; } = String.Empty;

        /// <summary>
        /// Tipo de documento de identificação (CC ou Passaporte) para abranger alunos internacionais.
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Tipo de Documento")]
        public EnumTipoDocumentoIdentificacao TipoDocumento { get; set; } = EnumTipoDocumentoIdentificacao.CartaoCidadao;

        /// <summary>
        /// Número do documento de identificação (pode conter letras e números no caso de passaportes).
        /// </summary>
        [Required(ErrorMessage = "O número do documento é de preenchimento obrigatório.")]
        [StringLength(30, ErrorMessage = "O {0} não pode exceder os {1} caracteres.")]
        [Display(Name = "Número do Documento")]
        public string NumeroDocumento { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação: Relação Muitos-para-Muitos com os cursos em que o aluno está inscrito.
        /// </summary>
        [Display(Name = "Cursos Inscritos")]
        public ICollection<Curso> Cursos { get; set; } = [];

        /// <summary>
        /// Propriedade de navegação: Relação 1-para-Muitos com as candidaturas submetidas pelo aluno.
        /// </summary>
        [Display(Name = "Candidaturas")]
        public virtual ICollection<Candidatura> Candidaturas { get; set; } = [];

    }
}
