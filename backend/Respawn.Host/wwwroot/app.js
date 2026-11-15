// The whole front of the skeleton: who am I, sign in, register, sign out. It only calls the public API.
const $ = (id) => document.getElementById(id);
let registering = false;

async function api(path, body) {
  const options = body === undefined ? {} : {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  };
  const response = await fetch(`/api/v1${path}`, options);
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(data?.error?.message ?? `The server answered ${response.status}.`);
    error.status = response.status;
    throw error;
  }
  return data;
}

function setMode(register, allowed) {
  registering = register;
  $("form-title").textContent = register ? "Create an account" : "Sign in";
  $("submit").textContent = register ? "Create the account" : "Sign in";
  $("display-name-field").hidden = !register;
  $("display-name-field").querySelector("input").required = register;
  $("sign-in").elements.password.autocomplete = register ? "new-password" : "current-password";
  $("toggle").hidden = !allowed;
  $("toggle").textContent = register ? "I already have an account" : "Create an account";
}

async function refresh() {
  $("account").hidden = true;
  $("sign-in").hidden = true;
  try {
    const me = await api("/me");
    $("name").textContent = me.display_name;
    $("role").textContent = me.is_admin ? "(administrator)" : "";
    $("email").textContent = me.email ?? "";
    $("account").hidden = false;
    $("status").textContent = "";
  } catch (error) {
    if (error.status !== 401) {
      $("status").textContent = error.message;
      return;
    }
    const config = await api("/config");
    if (!config.local_sign_in) {
      $("status").textContent = "Local sign-in is disabled on this instance.";
      return;
    }
    setMode(false, config.registration_open);
    $("sign-in").hidden = false;
    $("status").textContent = "";
  }
}

$("toggle").addEventListener("click", () => setMode(!registering, true));

$("sign-in").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget.elements;
  const body = { email: form.email.value, password: form.password.value };
  if (registering) body.display_name = form.display_name.value;
  try {
    await api(registering ? "/auth/register" : "/auth/login", body);
    form.password.value = "";
    await refresh();
  } catch (error) {
    $("status").textContent = error.message;
  }
});

$("logout").addEventListener("click", async () => {
  await api("/auth/logout", {});
  await refresh();
});

refresh().catch((error) => { $("status").textContent = error.message; });
