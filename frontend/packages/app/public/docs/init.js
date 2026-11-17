// Starts Swagger UI on the description the API serves. A file of its own: the content
// security policy allows no inline script.
window.ui = SwaggerUIBundle({
  url: '/api/v1/openapi.json',
  dom_id: '#swagger-ui',
  deepLinking: true,
  presets: [SwaggerUIBundle.presets.apis],
  layout: 'BaseLayout',
  // No badge from a validator elsewhere: nothing on this page leaves for another origin.
  validatorUrl: null,
});
