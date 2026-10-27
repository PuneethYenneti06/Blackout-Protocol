using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Capsule : MonoBehaviour
{
    Rigidbody rb;
    public int velocit;
    public GameObject gameobj;
    // Start is called before the first frame update
    public float sensx, sensy, yRotation;
    void Start()
    {
        Debug.Log("Hello  World");
        rb = GetComponent<Rigidbody>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        rb.freezeRotation = true;
        gameobj = GameObject.Find("Main Camera");
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 velo = new Vector3(0, 0, 0);
        if (Input.GetKey(KeyCode.D)) velo.x = velocit;
        if (Input.GetKey(KeyCode.A)) velo.x = -velocit;
        if (Input.GetKey(KeyCode.S)) velo.z = -velocit;
        if (Input.GetKey(KeyCode.W)) velo.z = velocit;
        Vector3 velo2 = gameobj.transform.forward * velo.z + gameobj.transform.right * velo.x;
        if (Input.GetKeyDown("space"))
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 5, rb.linearVelocity.z);
        }
        /*if (Input.GetKeyDown("w"))
        {*/

        rb.linearVelocity = velo2;
    
    if (Input.GetKeyDown("a"))
    {
        rb.linearVelocity = velo2;
    }
if (Input.GetKeyDown("s"))
{
    rb.linearVelocity = velo2;
}
if (Input.GetKeyDown("d"))
{
    rb.linearVelocity = velo2;
}

    rb.linearVelocity = velo2;
float mousex = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensx;
yRotation += mousex;
transform.rotation = Quaternion.Euler(0, yRotation, 0);
      /*  rb.rotation = Quaternion.Euler(0, yRotation,�0);*/
    }
}