/* Reproducible GIF capture from the offline BOOKSPACE demo: no SQL Server access. */
const fs = require("fs");
const path = require("path");
const [port, url, folder] = process.argv.slice(2);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function run() {
  const targets = await fetch("http://127.0.0.1:" + port + "/json/list")
    .then(response => response.json());
  const tab = targets.find(target => target.type === "page");
  if (!tab) throw new Error("Chrome debugging page unavailable");
  const socket = new WebSocket(tab.webSocketDebuggerUrl);
  const pending = new Map();
  const errors = [];
  let serial = 0;
  let frame = 0;

  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.method === "Runtime.exceptionThrown") {
      errors.push(message.params.exceptionDetails.exception?.description ||
        message.params.exceptionDetails.text);
    }
    if (!message.id || !pending.has(message.id)) return;
    const request = pending.get(message.id);
    pending.delete(message.id);
    message.error ? request.reject(new Error(message.error.message)) :
      request.resolve(message.result);
  };
  await new Promise((resolve, reject) => {
    socket.addEventListener("open", resolve, {once: true});
    socket.addEventListener("error", reject, {once: true});
  });

  function send(method, params = {}) {
    const id = ++serial;
    return new Promise((resolve, reject) => {
      pending.set(id, {resolve, reject});
      socket.send(JSON.stringify({id, method, params}));
    });
  }
  async function evaluate(expression) {
    const result = await send("Runtime.evaluate",
      {expression, returnByValue: true, awaitPromise: true});
    if (result.exceptionDetails) throw new Error(
      result.exceptionDetails.exception?.description || result.exceptionDetails.text);
    return result.result.value;
  }
  function assert(value, label) {
    if (!value) throw new Error("Failed: " + label);
    console.log("PASS " + label);
  }
  async function capture(times) {
    await sleep(140);
    const result = await send("Page.captureScreenshot",
      {format: "png", captureBeyondViewport: false, fromSurface: true});
    const bytes = Buffer.from(result.data, "base64");
    for (let i = 0; i < times; ++i) {
      fs.writeFileSync(path.join(folder, String(frame++).padStart(3, "0") + ".png"), bytes);
    }
  }

  await send("Page.enable");
  await send("Runtime.enable");
  await send("Emulation.setDeviceMetricsOverride",
    {width: 1280, height: 790, deviceScaleFactor: 1, mobile: false});
  await send("Page.navigate", {url});
  await sleep(900);

  assert(await evaluate('document.querySelectorAll(".book-card").length') === 12,
    "twelve clearly fictional demo books");
  assert(await evaluate('document.querySelector("#__bookspace_demo_notice")?.textContent.includes("未连接数据库")'),
    "visible offline-demo disclosure");
  await capture(4);

  await evaluate('document.querySelector("#search-input").value="算";document.querySelector("#search-input").dispatchEvent(new Event("input",{bubbles:true}))');
  await capture(2);
  await evaluate('document.querySelector("#search-input").value="算法";document.querySelector("#search-input").dispatchEvent(new Event("input",{bubbles:true}))');
  assert(await evaluate('document.querySelectorAll(".book-card").length') === 1,
    "search filters books");
  await capture(4);
  await evaluate('document.querySelector("#search-input").value="";document.querySelector("#search-input").dispatchEvent(new Event("input",{bubbles:true}))');
  await capture(2);

  await evaluate('Array.from(document.querySelectorAll("#category-filters button")).find(e=>e.textContent.trim().startsWith("设计美学")).click()');
  assert(await evaluate('document.querySelectorAll(".book-card").length') === 3,
    "category filter works");
  await capture(4);
  await evaluate('Array.from(document.querySelectorAll("#category-filters button")).find(e=>e.textContent.trim().startsWith("全部书籍")).click()');
  await capture(2);

  await evaluate('document.querySelector(".book-card-link").click()');
  assert(await evaluate('!document.querySelector("#detail-overlay").hidden'),
    "book detail panel works");
  await capture(5);
  await evaluate('document.querySelector("#close-detail").click()');
  await capture(2);

  await evaluate('document.querySelector("#view-list").click()');
  assert(await evaluate('document.querySelectorAll("#book-list tr").length') === 12,
    "list view works");
  await capture(4);
  await evaluate('document.querySelector("#view-gallery").click()');
  await capture(4);

  assert(errors.length === 0, "zero uncaught JavaScript exceptions");
  console.log("FRAME_COUNT=" + frame);
  socket.close();
}

run().catch(error => {console.error(error); process.exit(1);});