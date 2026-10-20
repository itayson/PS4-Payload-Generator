# TaySon Proxy Lab — 0.1 experimental

Aplicativo **Android separado** do PS4 Payload Generator, com applicationId `com.tayson.proxylab`.
Não usa Cloudflare Worker. Não mexe no APK 3.4.3 nem no firmware do PS4.

## Objetivo

1. Resolver URLs HTTP(S) de arquivo PKG, inclusive botões de páginas MediaFire quando disponíveis no HTML.
2. Verificar assinatura do PKG (`7F 43 4E 54`) e resposta `206 Content-Range: bytes 0-3/...` com transferência limitada.
3. Iniciar proxy local, oferecendo URL do tipo `http://IP_DO_CELULAR:8765/pkg/TOKEN/file.pkg`.
4. Preservar os bytes do provedor e transmitir `Range` ao origin sem fabricar 206.

## Escopo e limites

- **Não inclui payload/ELF, descoberta do console nem registro BGFT.** Use a URL obtida com uma ferramenta autorizada de download remoto já operacional no PS4.
- Só aceita URL pública HTTP(S) nas portas 80 e 443. URL privada e localhost são recusadas para evitar SSRF.
- Provedores cuja página dependa de JavaScript, cookies ou API autenticada podem não funcionar nesta versão.
- Serviço enquanto a Activity está aberta. Mantenha tela ligada, app aberto e ambos dispositivos na mesma rede local.
- Token aleatório no path protege endpoint contra acessos casuais, mas não é autenticação forte. Não exponha a porta na internet.
- O laboratório é experimental, não representa download/instalação validada no PS4.
- Usa assinatura padrão de **debug**, portanto não substitui nem atualiza o APK original.

## Build remoto

`gradle -p native-proxy-lab :app:assembleDebug` (JDK 17, Android SDK 35, Gradle 8.9, AGP 8.7.3).
O GitHub Actions armazena o APK como artifact. O fluxo não publica releases.

## Segurança e funcionamento

- Sem cookies, login ou tokens embutidos.
- Não persiste URLs de provedor em logs locais nem telemetria remota.
- Streaming por 64 KiB sem baixar um PKG inteiro no Android.
- Cabeçalhos HTTP de ranges são encaminhados mantendo status origin; respostas não compatíveis geram erro.

## Testes a executar no telefone e PS4

- URL direta HTTPS de PKG legítimo com Range.
- Link de página MediaFire que exponha o botão de download no HTML.
- GET `/pkg/TOKEN/file.pkg` com `Range: bytes=0-3`: esperar 206 com quatro bytes `7F434E54`.
- GET com `Range: bytes=1048576-1048580`: conferir bytes e `Content-Range`.
- Teste de download real através do BGFT: não executado no CI, exige o console.
