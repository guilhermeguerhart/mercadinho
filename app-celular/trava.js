// Trava do app (pedido do Guilherme em 05/11): o app continua logado, mas ao abrir (ou depois de 5 minutos fora) pede a
// digital, o rosto ou o PIN do próprio celular, em vez da senha do sistema. Usa o desbloqueio do aparelho (WebAuthn,
// "autenticador da plataforma"). É uma trava local: a sessão do sistema continua a mesma até tocar em Sair.
"use strict";

const TRAVA_MINUTOS = 5;
let travaSaiuEm = null;

function travaLigada() { return !!lerLocal("trava-credencial"); }

async function travaDisponivel() {
  try {
    return !!window.PublicKeyCredential && await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
  } catch {
    return false;
  }
}

function aleatorio(n) { return crypto.getRandomValues(new Uint8Array(n)); }
function paraTexto(buf) { return btoa(String.fromCharCode(...new Uint8Array(buf))); }
function deTexto(t) { return Uint8Array.from(atob(t), (c) => c.charCodeAt(0)); }

/** Liga a trava: o celular cria uma chave só deste app, protegida pela digital, rosto ou PIN. */
async function ligarTrava() {
  try {
    const cred = await navigator.credentials.create({
      publicKey: {
        challenge: aleatorio(32),
        rp: { name: "Mercado Gerência" },
        user: { id: aleatorio(16), name: estado.perfil?.usuario ?? "gerente", displayName: estado.perfil?.nome ?? "Gerente" },
        pubKeyCredParams: [{ type: "public-key", alg: -7 }, { type: "public-key", alg: -257 }],
        authenticatorSelection: { authenticatorAttachment: "platform", userVerification: "required", residentKey: "discouraged" },
        timeout: 60000,
      },
    });
    gravarLocal("trava-credencial", paraTexto(cred.rawId));
    return true;
  } catch {
    return false;
  }
}

function desligarTrava() { try { localStorage.removeItem("mercado-trava-credencial"); } catch { /* modo privado */ } }

/** Mostra a tela de trava e espera o desbloqueio. "Entrar com a senha" sai da conta. */
function desbloquear() {
  return new Promise((resolve) => {
    $("telaApp").hidden = true;
    $("telaLogin").hidden = true;
    $("telaTrava").hidden = false;
    $("erroTrava").textContent = "";
    const tentar = async () => {
      $("botaoDesbloquear").disabled = true;
      try {
        await navigator.credentials.get({
          publicKey: {
            challenge: aleatorio(32),
            allowCredentials: [{ type: "public-key", id: deTexto(lerLocal("trava-credencial")) }],
            userVerification: "required",
            timeout: 60000,
          },
        });
        $("telaTrava").hidden = true;
        resolve(true);
      } catch {
        $("erroTrava").textContent = "Não foi desbloqueado. Tente de novo ou entre com a senha.";
      } finally {
        $("botaoDesbloquear").disabled = false;
      }
    };
    $("botaoDesbloquear").onclick = tentar;
    $("botaoTravaSenha").onclick = async () => {
      desligarTrava();
      $("telaTrava").hidden = true;
      await sb.auth.signOut();
      resolve(false);
    };
    tentar();
  });
}

// Saiu do app por mais de 5 minutos: trava de novo ao voltar
document.addEventListener("visibilitychange", async () => {
  if (document.hidden) { travaSaiuEm = Date.now(); return; }
  if (!travaLigada() || $("telaApp").hidden || travaSaiuEm == null) return;
  if (Date.now() - travaSaiuEm >= TRAVA_MINUTOS * 60000) {
    travaSaiuEm = null;
    if (await desbloquear()) $("telaApp").hidden = false;
  }
});
