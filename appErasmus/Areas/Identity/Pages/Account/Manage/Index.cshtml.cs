#nullable disable

using appErasmus.Data;
using appErasmus.Models;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace appErasmus.Areas.Identity.Pages.Account.Manage {
    public class IndexModel : PageModel {
        private readonly UserManager<Utilizador> _userManager;
        private readonly SignInManager<Utilizador> _signInManager;
        private readonly ApplicationDbContext _context;

        public IndexModel(
            UserManager<Utilizador> userManager,
            SignInManager<Utilizador> signInManager,
            ApplicationDbContext context) {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        public string Username { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        public List<SelectListItem> Cursos { get; set; } = new();
        public bool IsCoordenador { get; set; }

        public List<CursoCoordenadoViewModel> CursosCoordenados { get; set; } = new();

        public class CursoCoordenadoViewModel {
            public string CodigoCurso { get; set; } = string.Empty;
            public string NomeCurso { get; set; } = string.Empty;
            public string Escola { get; set; } = string.Empty;
            public string Grau { get; set; } = string.Empty;
        }

        public class InputModel {
            [Display(Name = "Nome Completo")]
            public string NomeCompleto { get; set; }

            [Display(Name = "Número de Aluno")]
            public int? NumeroAluno { get; set; }

            [Display(Name = "Ano Curricular")]
            public int? AnoCurricular { get; set; }

            [Display(Name = "NIF")]
            [RegularExpression(@"^\d{9}$", ErrorMessage = "O NIF deve conter exatamente 9 dígitos.")]
            public string NIF { get; set; }

            [Display(Name = "Tipo de Documento")]
            public EnumTipoDocumentoIdentificacao? TipoDocumento { get; set; }

            [Display(Name = "Número do Documento")]
            [Required(ErrorMessage = "O {0} é obrigatório.")]
            [StringLength(20, ErrorMessage = "O {0} não pode exceder os {1} caracteres.")] // Limite anti-hacker
            public string NumeroDocumento { get; set; }

            [Display(Name = "Cursos")]
            [Required(ErrorMessage = "Deves selecionar pelo menos um curso.")]
            public List<int> CursosFK { get; set; } = new();

            // Serve para a vista saber se deve desenhar os campos de aluno ou não
            public bool IsAluno { get; set; }
        }

        private async Task LoadAsync(Utilizador user) {
            var userName = await _userManager.GetUserNameAsync(user);
            Username = userName;

            Input = new InputModel {
                NomeCompleto = user.NomeCompleto,
                IsAluno = false
            };

            IsCoordenador = await _userManager.IsInRoleAsync(user, "CoordenadorErasmus");

            // Se for do tipo Aluno, carrega os dados extra!
            if (user is Aluno aluno) {
                Input.IsAluno = true;
                Input.NumeroAluno = aluno.NumeroAluno;
                Input.AnoCurricular = aluno.AnoCurricular;
                Input.NIF = aluno.NIF;
                Input.TipoDocumento = aluno.TipoDocumento;
                Input.NumeroDocumento = aluno.NumeroDocumento;

                Input.CursosFK = await _context.Users
                    .OfType<Aluno>()
                    .Where(a => a.Id == aluno.Id)
                    .SelectMany(a => a.Cursos)
                    .Select(c => c.Id)
                    .ToListAsync();

                await CarregarCursosAsync();
            }

            if (IsCoordenador) {
                CursosCoordenados = await _context.Cursos
                    .Include(c => c.Escola)
                    .Include(c => c.GrauCurso)
                    .Where(c => c.CoordenadorFK == user.Id && c.IsActive)
                    .OrderBy(c => c.Escola.Nome)
                    .ThenBy(c => c.NomePTCurso)
                    .Select(c => new CursoCoordenadoViewModel {
                        CodigoCurso = c.CodigoCurso,
                        NomeCurso = c.NomePTCurso,
                        Escola = c.Escola.Nome,
                        Grau = c.GrauCurso.Nome
                    })
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnGetAsync() {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync() {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (user is not Aluno aluno) {
                return Forbid();
            }

            if (!ModelState.IsValid) {
                await LoadAsync(user);
                return Page();
            }

            // Se for aluno, faz validações extra e atualiza os dados
            if (Input.IsAluno) {

                // 1. Limpeza preventiva de inputs (Espaços e Traços)
                var docLimpo = Input.NumeroDocumento?.Replace(" ", "").Replace("-", "");
                Input.NumeroDocumento = docLimpo; // Aplica a limpeza ao input antes de gravar/validar

                // 2. Validações de Tamanho do CC
                if (Input.TipoDocumento == EnumTipoDocumentoIdentificacao.CartaoCidadao) {
                    if (docLimpo != null && (docLimpo.Length < 8 || docLimpo.Length > 12)) {
                        ModelState.AddModelError("Input.NumeroDocumento", "O número do Cartão de Cidadão deve ter entre 8 e 12 caracteres (incluindo dígitos de controlo).");
                    }
                }

                // INÍCIO DAS VALIDAÇÕES DE UNICIDADE NA BASE DE DADOS

                // Validação de NIF Único (Procura NIFs iguais cujo dono não seja o aluno que está a editar)
                var nifEmUso = await _context.Users.OfType<Aluno>()
                    .AnyAsync(a => a.NIF == Input.NIF && a.Id != aluno.Id);

                if (nifEmUso) {
                    ModelState.AddModelError("Input.NIF", "Já existe outro estudante registado com este NIF no portal.");
                }

                // Validação de Documento Único (CC ou Passaporte)
                var documentoEmUso = await _context.Users.OfType<Aluno>()
                    .AnyAsync(a => a.NumeroDocumento == Input.NumeroDocumento && a.Id != aluno.Id);

                if (documentoEmUso) {
                    ModelState.AddModelError("Input.NumeroDocumento", "Este número de documento já se encontra associado a outro estudante.");
                }

                // FIM DAS VALIDAÇÕES DE UNICIDADE

                // Se houver erros nas regras acima, devolvemos a página ANTES de gravar!
                if (!ModelState.IsValid) {
                    await LoadAsync(user);
                    return Page();
                }

                aluno.AnoCurricular = Input.AnoCurricular ?? 1;
                aluno.NIF = Input.NIF;
                aluno.TipoDocumento = Input.TipoDocumento ?? EnumTipoDocumentoIdentificacao.CartaoCidadao;
                aluno.NumeroDocumento = Input.NumeroDocumento; // Podemos gravar o "docLimpo" se quiserermos a BD mais limpa

                Input.CursosFK ??= new List<int>();

                if (!Input.CursosFK.Any()) {
                    ModelState.AddModelError("Input.CursosFK", "Deves selecionar pelo menos um curso.");
                    await LoadAsync(user);
                    return Page();
                }

                var cursosValidos = await _context.Cursos
                    .Where(c => Input.CursosFK.Contains(c.Id) && c.IsActive)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (cursosValidos.Count != Input.CursosFK.Distinct().Count()) {
                    ModelState.AddModelError("Input.CursosFK", "Um ou mais cursos selecionados não são válidos.");
                    await LoadAsync(user);
                    return Page();
                }

                var alunoComCursos = await _context.Users
                    .OfType<Aluno>()
                    .Include(a => a.Cursos)
                    .FirstOrDefaultAsync(a => a.Id == aluno.Id);

                if (alunoComCursos == null) {
                    return NotFound();
                }

                var idsNovos = Input.CursosFK.Distinct().ToList();

                var cursosSelecionados = await _context.Cursos
                    .Where(c => idsNovos.Contains(c.Id) && c.IsActive)
                    .ToListAsync();

                alunoComCursos.Cursos.Clear();

                foreach (var curso in cursosSelecionados) {
                    alunoComCursos.Cursos.Add(curso);
                }
            }

            await _userManager.UpdateAsync(user);
            await _context.SaveChangesAsync();
            await _signInManager.RefreshSignInAsync(user);

            StatusMessage = "O teu perfil foi atualizado com sucesso.";
            return RedirectToPage();
        }

        private async Task CarregarCursosAsync() {
            var listaCursos = await _context.Cursos
                .Include(c => c.Escola)
                .Include(c => c.GrauCurso)
                .Where(c => c.IsActive)
                .OrderBy(c => c.Escola!.Nome)
                .ThenBy(c => c.GrauCurso!.Nome)
                .ThenBy(c => c.NomePTCurso)
                .Select(c => new {
                    Id = c.Id,
                    Nome = c.NomePTCurso + " (" + c.GrauCurso!.Nome + ")",
                    GrupoOrganizado = c.Escola!.Nome
                })
                .ToListAsync();

            var cursosSelecionados = Input?.CursosFK ?? [];

            Cursos = new SelectList(
                listaCursos,
                "Id",
                "Nome",
                null,
                "GrupoOrganizado"
            ).ToList();

            foreach (var curso in Cursos) {
                if (int.TryParse(curso.Value, out var cursoId) &&
                    cursosSelecionados.Contains(cursoId)) {
                    curso.Selected = true;
                }
            }
        }
    }
}